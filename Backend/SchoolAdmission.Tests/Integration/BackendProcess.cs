using System.Diagnostics;
using System.Text.RegularExpressions;

namespace SchoolAdmission.Tests.Integration;

// Runs the built API on an ephemeral loopback port. Only processes started here
// are stopped; connection settings are passed in the environment, never logged.
internal sealed class BackendProcess(Process process, HttpClient client) : IAsyncDisposable
{
    public HttpClient Client { get; } = client;
    public int ProcessId => process.Id;

    public static async Task<BackendProcess> StartAsync(string connectionString, int durationSeconds = 600)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SchoolAdmission.slnx"))) directory = directory.Parent;
        if (directory is null) throw new InvalidOperationException("Cannot locate the solution root for the API process test.");
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var framework = new DirectoryInfo(AppContext.BaseDirectory).Name;
        var apiDirectory = Path.Combine(directory.FullName, "Backend", "SchoolAdmission.Api");
        var assembly = Path.Combine(apiDirectory, "bin", configuration, framework, "SchoolAdmission.Api.dll");
        if (!File.Exists(assembly)) throw new InvalidOperationException("Build the solution before running API process tests.");

        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = apiDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add(assembly);
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        startInfo.Environment["DOTNET_ENVIRONMENT"] = "Production";
        startInfo.Environment["Admin__AccessKey"] = FoundationApiFactory.AdminKey;
        startInfo.Environment["ASPNETCORE_URLS"] = "http://127.0.0.1:0";
        startInfo.Environment["ConnectionStrings__DefaultConnection"] = connectionString;
        startInfo.Environment["HOLD_DURATION_SECONDS"] = durationSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        startInfo.Environment["Expiration__IntervalMilliseconds"] = "100";
        startInfo.Environment["Expiration__BatchSize"] = "1";
        startInfo.Environment["Logging__LogLevel__Default"] = "Warning";
        startInfo.Environment["Logging__LogLevel__Microsoft.Hosting.Lifetime"] = "Information";
        var ready = new TaskCompletionSource<Uri>(TaskCreationOptions.RunContinuationsAsynchronously);
        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, args) =>
        {
            var match = Regex.Match(args.Data ?? "", @"^\s*Now listening on: (http://127\.0\.0\.1:\d+)\s*$");
            if (match.Success) ready.TrySetResult(new Uri(match.Groups[1].Value));
        };
        process.ErrorDataReceived += (_, _) => { }; // Drain stderr without exposing database/configuration values.
        process.Exited += (_, _) => ready.TrySetException(new InvalidOperationException("API process exited before reporting startup."));
        var started = false;
        try
        {
            started = process.Start();
            if (!started) throw new InvalidOperationException("Could not start the API process.");
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            var address = await ready.Task.WaitAsync(TimeSpan.FromSeconds(25));
            var client = new HttpClient { BaseAddress = address, Timeout = TimeSpan.FromSeconds(15) };
            client.DefaultRequestHeaders.Add("X-Admin-Key", FoundationApiFactory.AdminKey);
            return new BackendProcess(process, client);
        }
        catch
        {
            if (started && !process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
            process.Dispose();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        if (!process.HasExited) process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        process.Dispose();
    }
}
