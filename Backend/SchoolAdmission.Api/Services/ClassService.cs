using Microsoft.EntityFrameworkCore;
using Npgsql;
using SchoolAdmission.Api.Data;
using SchoolAdmission.Api.DTOs.Responses;
using SchoolAdmission.Api.Models.Entities;

namespace SchoolAdmission.Api.Services;

public sealed class ClassService(AppDbContext dbContext)
{
    // Null means the database rejected the name as a duplicate, including a racing insert.
    public async Task<CreateClassResponse?> CreateAsync(string name, int totalSeats, CancellationToken cancellationToken)
    {
        var admissionClass = new AdmissionClass { Name = name, TotalSeats = totalSeats };
        dbContext.AdmissionClasses.Add(admissionClass);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "ux_admission_classes_name" })
        {
            dbContext.Entry(admissionClass).State = EntityState.Detached;
            return null;
        }

        return new CreateClassResponse(admissionClass.Id, admissionClass.Name, admissionClass.TotalSeats);
    }

    public Task<List<ClassSummaryResponse>> GetAllAsync(CancellationToken cancellationToken) =>
        SeatSummaries().OrderBy(summary => summary.Name).ThenBy(summary => summary.Id).ToListAsync(cancellationToken);

    public Task<ClassSummaryResponse?> GetByIdAsync(Guid classId, CancellationToken cancellationToken) =>
        SeatSummaries().SingleOrDefaultAsync(summary => summary.Id == classId, cancellationToken);

    private IQueryable<ClassSummaryResponse> SeatSummaries() => dbContext.Database.SqlQuery<ClassSummaryResponse>($"""
        WITH database_time AS MATERIALIZED (SELECT clock_timestamp() AS value)
        SELECT
            admission_class.id AS "Id",
            admission_class.name AS "Name",
            admission_class.total_seats AS "TotalSeats",
            occupied.held::integer AS "HeldSeats",
            occupied.confirmed::integer AS "ConfirmedSeats",
            GREATEST(0, admission_class.total_seats - occupied.held - occupied.confirmed)::integer AS "AvailableSeats"
        FROM "AdmissionClasses" AS admission_class
        CROSS JOIN database_time AS db_time
        CROSS JOIN LATERAL (
            SELECT
                COUNT(*) FILTER (WHERE hold.status = 'HELD' AND hold.expires_at > db_time.value) AS held,
                COUNT(*) FILTER (WHERE hold.status = 'CONFIRMED') AS confirmed
            FROM "AdmissionHolds" AS hold
            WHERE hold.class_id = admission_class.id
        ) AS occupied
        """);
}
