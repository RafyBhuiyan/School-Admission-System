using Microsoft.EntityFrameworkCore;
using Npgsql;
using SchoolAdmission.Api.Data;
using SchoolAdmission.Api.DTOs.Responses;
using SchoolAdmission.Api.Enums;
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
        SeatSummaries(dbContext.AdmissionClasses).ToListAsync(cancellationToken);

    public Task<ClassSummaryResponse?> GetByIdAsync(Guid classId, CancellationToken cancellationToken) =>
        SeatSummaries(dbContext.AdmissionClasses.Where(admissionClass => admissionClass.Id == classId))
            .SingleOrDefaultAsync(cancellationToken);

    private IQueryable<ClassSummaryResponse> SeatSummaries(IQueryable<AdmissionClass> classes)
    {
        // Keep PostgreSQL's wall clock in the same query; EF translates the rest from LINQ.
        var databaseTime = dbContext.Database.SqlQuery<DateTime>($"SELECT clock_timestamp() AS \"Value\"");

        return from now in databaseTime
               from admissionClass in classes
               let held = admissionClass.Holds.Count(hold =>
                   hold.Status == AdmissionHoldStatus.HELD && hold.ExpiresAt > now)
               let confirmed = admissionClass.Holds.Count(hold =>
                   hold.Status == AdmissionHoldStatus.CONFIRMED)
               orderby admissionClass.Name, admissionClass.Id
               select new ClassSummaryResponse(
                   admissionClass.Id,
                   admissionClass.Name,
                   admissionClass.TotalSeats,
                   held,
                   confirmed,
                   Math.Max(0, admissionClass.TotalSeats - held - confirmed));
    }
}
