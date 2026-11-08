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
        studentId = studentId?.Trim();
        if (Request.Headers["X-Student-Id"].Count != 1 || string.IsNullOrEmpty(studentId) || studentId.Length > 64)
        {
            ModelState.AddModelError("X-Student-Id", "Supply one non-empty student ID of at most 64 characters.");
            return ValidationProblem(ModelState);
        }

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
}
