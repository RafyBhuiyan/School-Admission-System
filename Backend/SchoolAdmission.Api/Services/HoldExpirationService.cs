using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SchoolAdmission.Api.Configuration;
using SchoolAdmission.Api.Data;
using SchoolAdmission.Api.Enums;

namespace SchoolAdmission.Api.Services;

public sealed class HoldExpirationService(AppDbContext dbContext, IOptions<ExpirationOptions> options)
{
    public async Task<int> ExpireBatchAsync(CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        // Lock only a bounded set of overdue rows. Other workers and lifecycle
        // requests can keep processing rows we skip because they are locked.
        var ids = await dbContext.Database.SqlQuery<Guid>($"""
            SELECT id AS "Value" FROM "AdmissionHolds"
            WHERE status = 'HELD' AND expires_at <= clock_timestamp()
            ORDER BY expires_at, id
            LIMIT {options.Value.BatchSize}
            FOR UPDATE SKIP LOCKED
            """).ToListAsync(cancellationToken);
        if (ids.Count == 0) return 0;

        var now = await dbContext.Database.SqlQuery<DateTime>($"SELECT clock_timestamp() AS \"Value\"")
            .SingleAsync(cancellationToken);
        var expired = await dbContext.AdmissionHolds
            .Where(hold => ids.Contains(hold.Id) && hold.Status == AdmissionHoldStatus.HELD && hold.ExpiresAt <= now)
            .ExecuteUpdateAsync(setters => setters.SetProperty(hold => hold.Status, AdmissionHoldStatus.EXPIRED), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return expired;
    }
}
