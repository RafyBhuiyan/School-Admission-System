using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SchoolAdmission.Api.DTOs.Requests;
using SchoolAdmission.Api.DTOs.Responses;
using SchoolAdmission.Api.Services;
using SchoolAdmission.Api.Services.Results;

namespace SchoolAdmission.Api.Controllers;

public sealed class HoldsController(HoldService holdService) : ControllerBase
{
    [HttpGet("me/holds")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(List<StudentHoldResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<List<StudentHoldResponse>>> GetForStudent(
        [FromHeader(Name = "X-Student-Id"), Required(ErrorMessage = "A student ID is required.")] string? studentId,
        CancellationToken cancellationToken)
    {
        studentId = ValidateStudentId(studentId);
        if (studentId is null) return ValidationProblem(ModelState);
        return Ok(await holdService.GetForStudentAsync(studentId, cancellationToken));
    }

    [HttpDelete("holds/{holdId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel(Guid holdId,
        [FromHeader(Name = "X-Student-Id"), Required(ErrorMessage = "A student ID is required.")] string? studentId,
        CancellationToken cancellationToken)
    {
        studentId = ValidateStudentId(studentId);
        if (studentId is null) return ValidationProblem(ModelState);
        return await holdService.CancelAsync(holdId, studentId, cancellationToken) switch
        {
            CancelHoldOutcome.Cancelled => NoContent(),
            CancelHoldOutcome.NotFound => Problem(statusCode: StatusCodes.Status404NotFound, title: "Hold not found."),
            CancelHoldOutcome.Forbidden => Problem(statusCode: StatusCodes.Status403Forbidden, title: "This hold belongs to another student."),
            CancelHoldOutcome.Conflict => Problem(statusCode: StatusCodes.Status409Conflict, title: "This hold cannot be cancelled."),
            _ => throw new InvalidOperationException("Unexpected cancellation outcome.")
        };
    }

    [HttpPost("holds/{holdId}/confirm")]
    [ProducesResponseType(typeof(ConfirmHoldResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ConfirmHoldResponse>> Confirm(Guid holdId,
        [FromHeader(Name = "X-Student-Id"), Required(ErrorMessage = "A student ID is required.")] string? studentId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ConfirmHoldRequest? request,
        CancellationToken cancellationToken)
    {
        studentId = ValidateStudentId(studentId);
        if (studentId is null) return ValidationProblem(ModelState);
        var result = await holdService.ConfirmAsync(holdId, studentId, cancellationToken);
        return result.Outcome switch
        {
            ConfirmHoldOutcome.Confirmed => Ok(result.Hold),
            ConfirmHoldOutcome.NotFound => Problem(statusCode: StatusCodes.Status404NotFound, title: "Hold not found."),
            ConfirmHoldOutcome.Forbidden => Problem(statusCode: StatusCodes.Status403Forbidden, title: "This hold belongs to another student."),
            ConfirmHoldOutcome.Conflict => Problem(statusCode: StatusCodes.Status409Conflict, title: "This hold cannot be confirmed."),
            _ => throw new InvalidOperationException("Unexpected confirmation outcome.")
        };
    }

    [HttpGet("holds/{holdId}")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(HoldDetailsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HoldDetailsResponse>> GetById(Guid holdId, CancellationToken cancellationToken)
    {
        var hold = await holdService.GetByIdAsync(holdId, cancellationToken);
        return hold is null
            ? Problem(statusCode: StatusCodes.Status404NotFound, title: "Hold not found.")
            : Ok(hold);
    }

    [HttpPost("classes/{classId}/holds")]
    [ProducesResponseType(typeof(CreateHoldResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CreateHoldResponse>> Create(Guid classId,
        [FromHeader(Name = "X-Student-Id"), Required(ErrorMessage = "A student ID is required.")] string? studentId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CreateHoldRequest? request,
        CancellationToken cancellationToken)
    {
        studentId = ValidateStudentId(studentId);
        if (studentId is null) return ValidationProblem(ModelState);

        var result = await holdService.CreateAsync(classId, studentId, cancellationToken);
        return result.Outcome switch
        {
            CreateHoldOutcome.Created => StatusCode(StatusCodes.Status201Created, result.Hold),
            CreateHoldOutcome.ClassNotFound => Problem(statusCode: StatusCodes.Status404NotFound, title: "Class not found."),
            CreateHoldOutcome.DuplicateApplication => Problem(statusCode: StatusCodes.Status409Conflict,
                title: "This student already has an active or confirmed application for this class."),
            CreateHoldOutcome.ClassFull => Problem(statusCode: StatusCodes.Status409Conflict, title: "No seat is available for this class."),
            _ => throw new InvalidOperationException("Unexpected hold creation outcome.")
        };
    }

    private string? ValidateStudentId(string? value)
    {
        var studentId = value?.Trim();
        if (Request.Headers["X-Student-Id"].Count == 1 && !string.IsNullOrEmpty(studentId) && studentId.Length <= 64)
            return studentId;
        ModelState.AddModelError("X-Student-Id", "Supply one non-empty student ID of at most 64 characters.");
        return null;
    }
}
