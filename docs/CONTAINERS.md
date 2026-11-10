# Container images

Build from the repository root with Docker in Linux-container mode:

```powershell
docker build --file Backend/SchoolAdmission.Api/Dockerfile --tag school-admission-api:local .
docker build --tag school-admission-frontend:local Frontend
```

The backend context is the repository root; the frontend context is `Frontend`. Ignore files exclude local output, dependencies, and environment files. Base-image digests are pinned in the Dockerfiles. Backend restore uses the committed NuGet lockfile with `--locked-mode`; frontend installation uses `npm ci`. Update image digests and lockfiles deliberately, then rebuild and verify. [NuGet locked restore](https://learn.microsoft.com/en-us/nuget/consume-packages/package-references-in-project-files#locking-dependencies) documents the dependency behavior.

| Image | Runtime | Container port | User |
| --- | --- | --- | --- |
| `school-admission-api:local` | ASP.NET Core 10; no SDK | 8080, all container interfaces | 1654 (`app`) |
| `school-admission-frontend:local` | Nginx serving the React production bundle; no Node.js | 8080 | `nginx` |

## Run against an existing database

Use a PostgreSQL database reachable from the API container. On Docker Desktop, `host.docker.internal` addresses the host; for another container on the same Docker network, use its service/container name and pass `--network` to both migration and API commands. Database `localhost` inside the API container means that container itself.

The following PowerShell example uses an existing host database. Replace its credentials and port before running:

```powershell
$containerConnection = "Host=host.docker.internal;Port=5432;Database=school_admission;Username=school_admission;Password=replace-with-your-local-password"
docker run --rm --env "ConnectionStrings__DefaultConnection=$containerConnection" --entrypoint /app/migrate school-admission-api:local
docker run --rm --name school-admission-api --publish 8080:8080 --env "ConnectionStrings__DefaultConnection=$containerConnection" --env Cors__AllowedOrigins__0=http://localhost:5173 --env HOLD_DURATION_SECONDS=600 school-admission-api:local
```

The API command stays in the foreground. Start the frontend in another terminal:

```powershell
docker run --rm --name school-admission-frontend --publish 5173:8080 --env API_BASE_URL=http://localhost:8080 school-admission-frontend:local
```

Open `http://localhost:5173`; the API is at `http://localhost:8080`. These example ports match the default CORS origin. Change `Cors__AllowedOrigins__0` when changing the browser's frontend origin. Backend startup does not apply migrations: run the bundle successfully first. Repeating it applies only pending migrations. The [EF migration bundle](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying#bundles) runs with the runtime already in the image, without installing an SDK or EF tool on the host.

The API image sets `ASPNETCORE_HTTP_PORTS=8080`. Avoid passing a local-only `ASPNETCORE_URLS=http://localhost:8080` into the container, since that would override its interface binding. Settings are supplied explicitly here; the root `.env` is not loaded automatically by `docker run`.

## Frontend configuration

The startup script generates `/usr/share/nginx/html/config.json` from `API_BASE_URL` before Nginx starts. The default is `http://localhost:8080`; recreating the container with a different value changes configuration without rebuilding JavaScript. Supply an absolute browser-reachable HTTP(S) URL, optionally with a path prefix. Docker-internal names such as `backend` generally cannot be resolved by the host browser.

The script JSON-escapes the value and rejects missing schemes, empty authorities, credentials, whitespace/control characters, queries, and fragments. The browser also validates the URL. Never put secrets in this public file. Nginx serves config with `Cache-Control: no-store`, index HTML with `no-cache`, and hashed assets with immutable caching. Unknown application routes fall back to the index; missing assets return 404.

`GET /healthz` checks the static server; the image uses it for its Docker health check. This does not check backend/database readiness. API readiness and database startup ordering are not orchestrated yet: root Docker Compose packaging and its persistence verification remain pending.

## Verification

On 11 November 2026, both images built and ran on Docker Desktop Linux/amd64. A disposable PostgreSQL 18 container shared an isolated network with the API. The bundle initialized the schema, and a second run was a no-op. The API answered through its published port; CORS accepted the container-served frontend origin. A real browser created a class, applied, confirmed one synthetic student's admission, and cancelled another's hold. The final counts were total 2, held 0, confirmed 1, available 1. The [container portal preview](evidence/container-portal.png) preserves the result.

All 115 frontend tests and lint passed inside the pinned Linux Node build stage. Production asset delivery, SPA fallback, missing-asset 404, health, cache headers, four runtime URL/escaping cases, and six rejected settings passed. Runtime metadata confirmed both non-root users and no SDK in the API image. [Actual verification output](evidence/container-images.txt) records the results and image IDs. CI now builds both images; a hosted CI run was not performed here.

The minimal ASP.NET image lacks the optional Kerberos library. Npgsql logged a GSSAPI probe warning but connected successfully with the demo's password authentication; Kerberos authentication is not configured or verified. Npgsql's [GSS encryption notes](https://www.npgsql.org/doc/release-notes/10.0.html) explain its preferred-mode fallback. The local Windows build also reported unavailable NuGet vulnerability metadata; the container restore and publish completed successfully. The unchanged backend's previous 220-test result was not rerun in full. Compose startup/persistence, other CPU architectures, mobile viewport, and a full browser-process restart remain unverified. Test containers/network were removed after verification; built local images were retained.
