using Microsoft.Extensions.Options;
using SchoolAdmission.Api.Configuration;
using SchoolAdmission.Api.Services;

namespace SchoolAdmission.Api.BackgroundServices;

public sealed class HoldExpirationWorker(IServiceScopeFactory scopeFactory, IOptions<ExpirationOptions> options,
    ILogger<HoldExpirationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var expired = await scope.ServiceProvider.GetRequiredService<HoldExpirationService>()
                    .ExpireBatchAsync(stoppingToken);
                if (expired > 0) logger.LogInformation("Expired {ExpiredCount} overdue holds.", expired);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                // Keep identifiers, connection values, and raw database messages out of worker logs.
                logger.LogError("Hold expiration batch failed ({ExceptionType}); retrying on the next cycle.",
                    exception.GetType().Name);
            }

            try { await Task.Delay(options.Value.IntervalMilliseconds, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
