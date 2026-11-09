using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using SchoolAdmission.Api.Configuration;
using SchoolAdmission.Api.Data;
using SchoolAdmission.Api.DTOs.Responses;
using SchoolAdmission.Api.Enums;
using SchoolAdmission.Api.Models.Entities;
using SchoolAdmission.Api.Services.Results;

namespace SchoolAdmission.Api.Services;

public sealed class HoldService(AppDbContext dbContext, IOptions<HoldOptions> options)
{
    public async Task<HoldDetailsResponse?> GetByIdAsync(Guid holdId, CancellationToken cancellationToken)
    {
        var databaseTime = dbContext.Database.SqlQuery<DateTime>($"SELECT clock_timestamp() AS \"Value\"");
        var details = await (from now in databaseTime
                             from hold in dbContext.AdmissionHolds
                             where hold.Id == holdId
                             select new
                             {
                                 hold.Id,
                                 hold.ClassId,
                                 Status = hold.Status == AdmissionHoldStatus.HELD && hold.ExpiresAt <= now
                                     ? AdmissionHoldStatus.EXPIRED : hold.Status,
                                 hold.ExpiresAt,
                                 ServerTime = now
                             }).SingleOrDefaultAsync(cancellationToken);

        // Expiry is effective immediately, even before a worker persists EXPIRED.
        return details is null ? null : new HoldDetailsResponse(
            details.Id, details.ClassId, details.Status.ToString(), details.ExpiresAt, details.ServerTime);
    }

    public async Task<CreateHoldResult> CreateAsync(Guid classId, string studentId, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        // All allocations for this class, including requests in other API instances,
        // must acquire this database lock before checking capacity.
        var admissionClass = await dbContext.AdmissionClasses
            .FromSql($"SELECT * FROM \"AdmissionClasses\" WHERE id = {classId} FOR UPDATE")
            .AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (admissionClass is null)
        {
            return new(CreateHoldOutcome.ClassNotFound);
        }

        // Read after the lock wait; transaction-start time could already be stale.
        var now = await dbContext.Database.SqlQuery<DateTime>($"SELECT clock_timestamp() AS \"Value\"")
            .SingleAsync(cancellationToken);
        await dbContext.AdmissionHolds
            .Where(hold => hold.ClassId == classId && hold.Status == AdmissionHoldStatus.HELD && hold.ExpiresAt <= now)
            .ExecuteUpdateAsync(setters => setters.SetProperty(hold => hold.Status, AdmissionHoldStatus.EXPIRED), cancellationToken);

        if (await dbContext.AdmissionHolds.AnyAsync(hold => hold.ClassId == classId && hold.StudentId == studentId &&
            (hold.Status == AdmissionHoldStatus.HELD || hold.Status == AdmissionHoldStatus.CONFIRMED), cancellationToken))
        {
            return new(CreateHoldOutcome.DuplicateApplication);
        }

        var occupied = await dbContext.AdmissionHolds.LongCountAsync(hold => hold.ClassId == classId &&
            (hold.Status == AdmissionHoldStatus.CONFIRMED ||
                (hold.Status == AdmissionHoldStatus.HELD && hold.ExpiresAt > now)), cancellationToken);
        if (occupied >= admissionClass.TotalSeats)
        {
            return new(CreateHoldOutcome.ClassFull);
        }

        var hold = new AdmissionHold
        {
            ClassId = classId,
            StudentId = studentId,
            Status = AdmissionHoldStatus.HELD,
            CreatedAt = now,
            ExpiresAt = now.AddSeconds(options.Value.DurationSeconds)
        };
        dbContext.AdmissionHolds.Add(hold);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "ux_admission_holds_class_student_active" })
        {
            // Final database safeguard. Disposing the uncommitted transaction also
            // rolls back expiry cleanup on this conflict, as on the earlier exits.
            dbContext.Entry(hold).State = EntityState.Detached;
            return new(CreateHoldOutcome.DuplicateApplication);
        }

        await transaction.CommitAsync(cancellationToken);
        return new(CreateHoldOutcome.Created,
            new CreateHoldResponse(hold.Id, classId, studentId, hold.Status.ToString(), hold.ExpiresAt));
    }
}
