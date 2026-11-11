using Microsoft.AspNetCore.Mvc;
using SchoolAdmission.Api.Common;
using SchoolAdmission.Api.Services;

namespace SchoolAdmission.Api.Controllers;

[Route("admin")]
[AdminOnly]
public sealed class AdminController(AdminApplicationService applications) : ControllerBase
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
        var result = await applications.GetApplicationsAsync(classId, status, page, pageSize, cancellationToken);
        return result is null
            ? Problem(statusCode: 400, title: "Invalid application filters or page.")
            : Ok(result);
    }
}
