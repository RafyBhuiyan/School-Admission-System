#requires -Version 7.0
[CmdletBinding()]
param([string] $ReportPath)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$runId = [guid]::NewGuid().ToString('N').Substring(0, 8)
$project = "school-admission-persistence-$runId"
$runRoot = Join-Path $repoRoot "artifacts/compose-persistence/$runId"
$sourceRoot = Join-Path $runRoot 'source'
$logPath = Join-Path $runRoot 'docker.log'
$ownsProject = $false
$report = [ordered]@{ Result = 'Running'; Project = $project; Source = 'Clean working-tree export; ignored/generated files and Git metadata excluded'; Checks = @() }
$controlledKeys = @('COMPOSE_PROJECT_NAME','COMPOSE_FILE','COMPOSE_ENV_FILES','COMPOSE_PROFILES',
    'POSTGRES_DB','POSTGRES_USER','POSTGRES_PASSWORD','FRONTEND_PORT','API_PORT','FRONTEND_ORIGIN',
    'API_BASE_URL','API_HTTP_PORT','FRONTEND_HTTP_PORT','HealthCheck__BaseUrl','Admin__AccessKey',
    'HOLD_DURATION_SECONDS','Expiration__IntervalMilliseconds','Expiration__BatchSize')
$savedEnvironment = @{}
foreach ($key in $controlledKeys) { $savedEnvironment[$key] = [Environment]::GetEnvironmentVariable($key, 'Process') }

function Assert-Check([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
    $report.Checks += $Message
    Write-Host "PASS: $Message"
}

function Get-FreePort {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    try {
        $listener.Start()
        return $listener.LocalEndpoint.Port
    }
    finally { $listener.Stop() }
}

function Invoke-Docker([string[]] $DockerArguments, [switch] $Capture) {
    Add-Content -LiteralPath $logPath -Value ("docker " + ($DockerArguments -join ' '))
    $lines = [Collections.Generic.List[string]]::new()
    & docker @DockerArguments 2>&1 | ForEach-Object {
        $line = $_.ToString()
        Add-Content -LiteralPath $logPath -Value $line
        if ($Capture) { $lines.Add($line) } else { Write-Host $line }
    }
    if ($LASTEXITCODE -ne 0) { throw "Docker command failed: $($DockerArguments -join ' ')" }
    if ($Capture) { return $lines.ToArray() }
}

function Get-Services {
    return @(Invoke-Docker -DockerArguments @('compose','ps','--all','--format','json') -Capture |
        ForEach-Object { $_ | ConvertFrom-Json })
}

function Assert-Ready($Services) {
    foreach ($name in @('postgres','backend','frontend')) {
        $service = $Services | Where-Object Service -eq $name
        Assert-Check ($service.State -eq 'running' -and $service.Health -eq 'healthy') "$name is running and healthy"
    }
    $migration = $Services | Where-Object Service -eq 'migrator'
    Assert-Check ($migration.State -eq 'exited' -and $migration.ExitCode -eq 0) 'Migration job completed successfully'
}

function Get-PublishedBase($Service) {
    $binding = @($Service.ports)[0]
    $hostName = $binding.host_ip
    if (-not $hostName -or $hostName -in @('0.0.0.0','::')) { $hostName = [Net.IPAddress]::Loopback.ToString() }
    return [UriBuilder]::new([Uri]::UriSchemeHttp, $hostName, [int]$binding.published).Uri
}

function Invoke-Api([string] $Method, [string] $Path, [string] $Student, $Body) {
    $arguments = @{ Method = $Method; Uri = [Uri]::new($script:apiBase, $Path.TrimStart('/')); TimeoutSec = 15 }
    if ($Student) { $arguments.Headers = @{ 'X-Student-Id' = $Student } }
    if ($Method -eq 'POST' -and $Path -eq 'classes') { $arguments.Headers = @{ 'X-Admin-Key' = $script:model.services.backend.environment.Admin__AccessKey } }
    if ($null -ne $Body) { $arguments.ContentType = 'application/json'; $arguments.Body = $Body | ConvertTo-Json -Compress }
    # Enumerate collection responses consistently before callers build snapshot arrays.
    $response = Invoke-RestMethod @arguments
    return $response
}

function Get-DatabaseSnapshot {
    $sql = @'
SELECT jsonb_build_object(
  'classes', COALESCE((SELECT jsonb_agg(to_jsonb(c) ORDER BY c.id) FROM "AdmissionClasses" c), '[]'::jsonb),
  'holds', COALESCE((SELECT jsonb_agg(to_jsonb(h) ORDER BY h.id) FROM "AdmissionHolds" h), '[]'::jsonb),
  'migrations', COALESCE((SELECT jsonb_agg(to_jsonb(m) ORDER BY m."MigrationId") FROM "__EFMigrationsHistory" m), '[]'::jsonb)
);
'@
    $snapshot = Invoke-Docker -DockerArguments @('compose','exec','--no-TTY','postgres','psql',
        '--username',$script:model.services.postgres.environment.POSTGRES_USER,
        '--dbname',$script:model.services.postgres.environment.POSTGRES_DB,
        '--tuples-only','--no-align','--command',$sql) -Capture
    return ($snapshot -join "`n").Trim()
}

function Get-ApiSnapshot {
    $histories = foreach ($student in $script:students) { @(Invoke-Api 'GET' 'me/holds' $student $null) }
    return [ordered]@{ Classes = @(Invoke-Api 'GET' 'classes' '' $null); Histories = @($histories) } | ConvertTo-Json -Depth 20 -Compress
}

try {
    New-Item -ItemType Directory -Path $sourceRoot -Force | Out-Null
    $report['Revision'] = (& git -C $repoRoot rev-parse HEAD).Trim()
    $files = ((& git -C $repoRoot ls-files --cached --others --exclude-standard -z) -join "`n").Split([char]0, [StringSplitOptions]::RemoveEmptyEntries)
    if ($LASTEXITCODE -ne 0) { throw 'Could not enumerate source files.' }
    foreach ($relative in $files) {
        if ($relative -match '(^|/)(node_modules|bin|obj|dist|TestResults|artifacts)(/|$)') { continue }
        if ($relative -match '(^|/)\.env($|\.)' -and [IO.Path]::GetFileName($relative) -ne '.env.example') { continue }
        $source = Join-Path $repoRoot $relative
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { continue }
        $destination = [IO.Path]::GetFullPath((Join-Path $sourceRoot $relative))
        if (-not $destination.StartsWith($sourceRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Source export escaped its directory.' }
        New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($destination)) -Force | Out-Null
        Copy-Item -LiteralPath $source -Destination $destination
    }
    foreach ($path in @('Frontend/node_modules','Frontend/dist','Backend/SchoolAdmission.Api/bin','Backend/SchoolAdmission.Api/obj','.git','.env')) {
        Assert-Check (-not (Test-Path -LiteralPath (Join-Path $sourceRoot $path))) "Clean export excludes $path"
    }
    $settings = Get-Content -LiteralPath (Join-Path $sourceRoot '.env.example') -Raw
    $settings = $settings -replace '(?m)^POSTGRES_DB=.*$', 'POSTGRES_DB=school_admission_persistence_tests'
    $settings = $settings -replace '(?m)^POSTGRES_USER=.*$', 'POSTGRES_USER=schema_test'
    $settings = $settings -replace '(?m)^Admin__AccessKey=.*$', 'Admin__AccessKey=synthetic-compose-administrator-key-32-characters'
    $settings = $settings -replace '(?m)^POSTGRES_PASSWORD=.*$', 'POSTGRES_PASSWORD=synthetic-compose-persistence-password'
    [IO.File]::WriteAllText((Join-Path $sourceRoot '.env'), $settings + "`nCOMPOSE_PROJECT_NAME=$project`n")
    foreach ($key in $controlledKeys) { Remove-Item -LiteralPath "Env:$key" -ErrorAction SilentlyContinue }
    $env:COMPOSE_PROJECT_NAME = $project
    $apiPort = Get-FreePort
    do { $frontendPort = Get-FreePort } while ($frontendPort -eq $apiPort)
    $env:API_PORT = [string]$apiPort
    $env:FRONTEND_PORT = [string]$frontendPort
    $env:API_BASE_URL = "http://localhost:$apiPort"
    Push-Location $sourceRoot
    try {
        Invoke-Docker -DockerArguments @('compose','config','--quiet')
        # Resolved configuration contains credentials; keep it out of logs and reports.
        $modelJson = & docker compose config --format json
        if ($LASTEXITCODE -ne 0) { throw 'Could not resolve Compose configuration.' }
        $script:model = ($modelJson -join "`n") | ConvertFrom-Json
        $volumeName = $model.volumes.'postgres-data'.name
        Assert-Check ($model.name -eq $project -and $volumeName -eq "${project}_postgres-data") 'Project and named volume are isolated'
        $existing = @(Invoke-Docker -DockerArguments @('ps','--all','--filter',"label=com.docker.compose.project=$project",'--quiet') -Capture)
        $existingVolumes = @(Invoke-Docker -DockerArguments @('volume','ls','--filter',"label=com.docker.compose.project=$project",'--quiet') -Capture)
        Assert-Check ($existing.Count -eq 0 -and $existingVolumes.Count -eq 0) 'No pre-existing test containers or volumes'
        $ownsProject = $true
        $script:apiBase = Get-PublishedBase $model.services.backend
        $frontendBase = Get-PublishedBase $model.services.frontend
        Invoke-Docker -DockerArguments @('compose','up','--build','--wait','--wait-timeout','120')
        $beforeServices = Get-Services
        Assert-Ready $beforeServices
        $emptyDatabase = Get-DatabaseSnapshot | ConvertFrom-Json
        Assert-Check (@($emptyDatabase.classes).Count -eq 0 -and @($emptyDatabase.holds).Count -eq 0) 'Fresh volume starts with no classes or applications'
        $frontend = Invoke-WebRequest -Uri $frontendBase -TimeoutSec 15
        $runtimeConfig = Invoke-RestMethod -Uri ([Uri]::new($frontendBase, 'config.json')) -TimeoutSec 15
        Assert-Check ($frontend.StatusCode -eq 200 -and ([Uri]$runtimeConfig.API_BASE_URL).Port -eq $apiBase.Port) 'Frontend assets and browser API configuration are available'
        $script:students = @('00000000000000001','00000000000000002','00000000000000003')
        $firstClass = Invoke-Api 'POST' 'classes' '' @{ name = 'Class 6 - Persistence morning'; totalSeats = 4 }
        $secondClass = Invoke-Api 'POST' 'classes' '' @{ name = 'Class 7 - Persistence afternoon'; totalSeats = 2 }
        $held = Invoke-Api 'POST' "classes/$($firstClass.id)/holds" $students[0] $null
        $confirmed = Invoke-Api 'POST' "classes/$($firstClass.id)/holds" $students[1] $null
        $null = Invoke-Api 'POST' "holds/$($confirmed.holdId)/confirm" $students[1] $null
        $cancelled = Invoke-Api 'POST' "classes/$($firstClass.id)/holds" $students[2] $null
        $null = Invoke-Api 'DELETE' "holds/$($cancelled.holdId)" $students[2] $null
        $otherClassHold = Invoke-Api 'POST' "classes/$($secondClass.id)/holds" $students[0] $null
        $null = Invoke-Api 'POST' "holds/$($otherClassHold.holdId)/confirm" $students[0] $null
        $beforeDatabase = Get-DatabaseSnapshot
        $beforeApi = Get-ApiSnapshot
        $rows = $beforeDatabase | ConvertFrom-Json
        Assert-Check (@($rows.classes).Count -eq 2 -and @($rows.holds).Count -eq 4) 'Created two classes and four applications through the API'
        Assert-Check (@($rows.holds | Where-Object status -eq 'HELD').Count -eq 1 -and @($rows.holds | Where-Object status -eq 'CONFIRMED').Count -eq 2 -and @($rows.holds | Where-Object status -eq 'CANCELLED').Count -eq 1) 'Active, confirmed, and cancelled records exist before shutdown'
        $summaries = (ConvertFrom-Json $beforeApi).Classes
        $morning = $summaries | Where-Object id -eq $firstClass.id
        $afternoon = $summaries | Where-Object id -eq $secondClass.id
        Assert-Check ($morning.heldSeats -eq 1 -and $morning.confirmedSeats -eq 1 -and $morning.availableSeats -eq 2 -and $afternoon.heldSeats -eq 0 -and $afternoon.confirmedSeats -eq 1 -and $afternoon.availableSeats -eq 1) 'Seat counts match held, confirmed, and cancelled applications'
        $volumeBefore = (Invoke-Docker -DockerArguments @('volume','inspect',$volumeName) -Capture | ConvertFrom-Json)[0]
        Invoke-Docker -DockerArguments @('compose','down')
        $afterDown = Get-Services
        $volumeAfterDown = (Invoke-Docker -DockerArguments @('volume','inspect',$volumeName) -Capture | ConvertFrom-Json)[0]
        Assert-Check ($afterDown.Count -eq 0) 'Ordinary down removed all four containers'
        Assert-Check ($volumeBefore.CreatedAt -eq $volumeAfterDown.CreatedAt) 'Ordinary down retained the original named volume'
        Invoke-Docker -DockerArguments @('compose','up','--build','--wait','--wait-timeout','120')
        $afterServices = Get-Services
        Assert-Ready $afterServices
        foreach ($old in $beforeServices) {
            $current = $afterServices | Where-Object Service -eq $old.Service
            Assert-Check ($old.ID -ne $current.ID) "$($old.Service) container was recreated"
        }
        $afterDatabase = Get-DatabaseSnapshot
        $afterApi = Get-ApiSnapshot
        Assert-Check ($beforeDatabase -ceq $afterDatabase) 'Database rows, identities, timestamps, statuses, and migration history survived exactly'
        Assert-Check ($beforeApi -ceq $afterApi) 'API class counts and all three students histories survived exactly'
        $holdAfter = Invoke-Api 'GET' "holds/$($held.holdId)" '' $null
        Assert-Check ($holdAfter.status -eq 'HELD' -and $holdAfter.expiresAt -eq $held.expiresAt) 'The active hold remained valid with its original deadline'
        $report['ClassCount'] = @($rows.classes).Count
        $report['ApplicationCount'] = @($rows.holds).Count
        $report['DatabaseSha256'] = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($beforeDatabase)))
        $report['ShutdownCommand'] = 'docker compose down (no volume deletion)'
        $report['StartupCommand'] = 'docker compose up --build --wait --wait-timeout 120'
        $report['Result'] = 'Passed'
    }
    finally {
        try {
            if ($ownsProject) {
                # This is separate from the ordinary down/up cycle under test.
                Invoke-Docker -DockerArguments @('compose','down','--volumes')
                $report['Cleanup'] = 'Only the isolated test project and its disposable volume were removed after the test'
            }
        }
        finally { Pop-Location }
    }
}
catch {
    $report['Result'] = 'Failed'
    $report['Error'] = $_.Exception.Message
    throw
}
finally {
    foreach ($key in $controlledKeys) {
        if ($null -eq $savedEnvironment[$key]) { Remove-Item -LiteralPath "Env:$key" -ErrorAction SilentlyContinue }
        else { [Environment]::SetEnvironmentVariable($key, $savedEnvironment[$key], 'Process') }
    }
    $report['VerifiedAtUtc'] = [DateTime]::UtcNow.ToString('O')
    $json = $report | ConvertTo-Json -Depth 10
    [IO.File]::WriteAllText((Join-Path $runRoot 'report.json'), $json)
    if ($ReportPath) {
        $reportFile = if ([IO.Path]::IsPathRooted($ReportPath)) { $ReportPath } else { Join-Path $repoRoot $ReportPath }
        [IO.File]::WriteAllText($reportFile, $json)
    }
    Write-Host $json
}
