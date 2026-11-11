using Microsoft.AspNetCore.Mvc;
using SchoolAdmission.Api.Common;

namespace SchoolAdmission.Api.Controllers;

[Route("admin/session")]
[AdminOnly]
public sealed class AdminController : ControllerBase
{
    [HttpGet]
    public IActionResult Verify()
    {
        Response.Headers.CacheControl = "no-store";
        return NoContent();
    }
}
