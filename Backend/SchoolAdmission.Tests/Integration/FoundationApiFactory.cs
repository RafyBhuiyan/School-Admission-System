using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SchoolAdmission.Api.BackgroundServices;
using Microsoft.Extensions.Hosting;

namespace SchoolAdmission.Tests.Integration;

internal sealed class FoundationApiFactory(
    string environment = "Development",
    IReadOnlyDictionary<string, string?>? overrides = null,
    bool runExpirationWorker = false) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            var settings = new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] =
                    "Host=localhost;Port=5432;Database=foundation_tests;Username=test;Password=synthetic-test-password",
                ["Cors:AllowedOrigins:0"] = "http://localhost:5173",
                ["HOLD_DURATION_SECONDS"] = "600",
                ["Expiration:IntervalMilliseconds"] = "1000",
                ["Expiration:BatchSize"] = "100"
            };
            if (overrides is not null)
            {
                foreach (var entry in overrides)
                {
                    settings[entry.Key] = entry.Value;
                }
            }

            configuration.AddInMemoryCollection(settings);
        });

        // Probe endpoints exist only in the test host, never in the deployed API.
        builder.ConfigureServices(services =>
        {
            services.AddControllers().AddApplicationPart(typeof(FoundationProbeController).Assembly);
            // Most endpoint tests need controlled stored states. Worker integration
            // tests explicitly enable the production hosted service.
            if (!runExpirationWorker)
            {
                foreach (var registration in services.Where(service => service.ServiceType == typeof(IHostedService)
                    && service.ImplementationType == typeof(HoldExpirationWorker)).ToArray())
                    services.Remove(registration);
            }
        });
    }
}
