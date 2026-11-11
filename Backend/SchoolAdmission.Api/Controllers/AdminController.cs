using Microsoft.AspNetCore.Mvc;
using SchoolAdmission.Api.Common;
using Microsoft.EntityFrameworkCore;
using SchoolAdmission.Api.Data;
using SchoolAdmission.Api.Enums;

namespace SchoolAdmission.Api.Controllers;

[Route("admin")]
[AdminOnly]
public sealed class AdminController(AppDbContext database) : ControllerBase
{
    [HttpGet("session")]
    public IActionResult Verify()
    {
        Response.Headers.CacheControl = "no-store";
        return NoContent();
    }

    [HttpGet("applications")]
    public async Task<IActionResult> Applications(Guid? classId, string? status, int page = 1, int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        Response.Headers.CacheControl = "no-store";
        if (page < 1 || page > 100000 || pageSize < 1 || pageSize > 100 ||
            status is not null && !new[] { "HELD", "CONFIRMED", "EXPIRED", "CANCELLED" }.Contains(status))
            return Problem(statusCode: 400, title: "Invalid application filters or page.");

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
            .Select(row => new { row.HoldId, row.ClassId, row.ClassName, row.StudentId,
                Status = row.Status.ToString(), row.CreatedAt, row.ExpiresAt }).ToListAsync(cancellationToken);
        return Ok(new { items, total, page, pageSize });
    }
}
