using Microsoft.EntityFrameworkCore;
using SchoolAdmission.Api.Data;
using SchoolAdmission.Api.DTOs.Responses;
using SchoolAdmission.Api.Enums;

namespace SchoolAdmission.Api.Services;

public sealed class AdminApplicationService(AppDbContext database)
{
    // Null indicates invalid filters; reject them before opening a database connection.
    public async Task<AdminApplicationsResponse?> GetApplicationsAsync(Guid? classId, string? status,
        int page, int pageSize, CancellationToken cancellationToken)
    {
        if (page < 1 || page > 100000 || pageSize < 1 || pageSize > 100 ||
            status is not null && !new[] { "HELD", "CONFIRMED", "EXPIRED", "CANCELLED" }.Contains(status))
            return null;

        var now = await database.Database.SqlQuery<DateTime>($"SELECT clock_timestamp() AS \"Value\"")
            .SingleAsync(cancellationToken);
        var query = database.AdmissionHolds.AsNoTracking().Select(hold => new
        {
            HoldId = hold.Id, hold.ClassId, ClassName = hold.AdmissionClass.Name, hold.StudentId,
            Status = hold.Status == AdmissionHoldStatus.HELD && hold.ExpiresAt <= now
                ? AdmissionHoldStatus.EXPIRED : hold.Status,
            hold.CreatedAt, hold.ExpiresAt
        });
        if (classId.HasValue) query = query.Where(row => row.ClassId == classId.Value);
        if (status is not null)
        {
            var selected = Enum.Parse<AdmissionHoldStatus>(status);
            query = query.Where(row => row.Status == selected);
        }
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(row => row.CreatedAt).ThenBy(row => row.HoldId)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(row => new AdminApplicationResponse(row.HoldId, row.ClassId, row.ClassName, row.StudentId,
                row.Status.ToString(), row.CreatedAt, row.ExpiresAt)).ToListAsync(cancellationToken);
        return new AdminApplicationsResponse(items, total, page, pageSize);
    }
}
