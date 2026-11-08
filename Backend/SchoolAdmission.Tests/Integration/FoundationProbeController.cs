using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace SchoolAdmission.Tests.Integration;

[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("__foundation-tests")]
public sealed class FoundationProbeController : ControllerBase
{
    [HttpGet("error")]
    public IActionResult Error() => throw new InvalidOperationException("synthetic-sensitive-detail");

    [HttpGet("bad-request")]
    public IActionResult BadRequestException() =>
        throw new BadHttpRequestException("synthetic-sensitive-detail", StatusCodes.Status400BadRequest);

    [HttpPost("validate")]
    public IActionResult Validate(FoundationProbeRequest request) => Ok(request);
}

public sealed class FoundationProbeRequest
{
    [Required]
    [StringLength(30)]
    public string? Name { get; set; }

    [Range(1, 100)]
    public int Seats { get; set; }
}
