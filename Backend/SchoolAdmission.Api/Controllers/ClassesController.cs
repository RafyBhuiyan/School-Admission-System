using Microsoft.AspNetCore.Mvc;
using SchoolAdmission.Api.DTOs.Requests;
using SchoolAdmission.Api.DTOs.Responses;
using SchoolAdmission.Api.Services;

namespace SchoolAdmission.Api.Controllers;

[Route("classes")]
public sealed class ClassesController(ClassService classService) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(CreateClassResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CreateClassResponse>> Create(
        CreateClassRequest request, CancellationToken cancellationToken)
    {
        var created = await classService.CreateAsync(request.Name!, request.TotalSeats!.Value, cancellationToken);
        return created is null
            ? Problem(statusCode: StatusCodes.Status409Conflict, title: "A class with that name already exists.")
            : CreatedAtAction(nameof(GetById), new { classId = created.Id }, created);
    }

    [HttpGet]
    [ProducesResponseType(typeof(List<ClassSummaryResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<ClassSummaryResponse>>> GetAll(CancellationToken cancellationToken) =>
        Ok(await classService.GetAllAsync(cancellationToken));

    [HttpGet("{classId}")]
    [ProducesResponseType(typeof(ClassSummaryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ClassSummaryResponse>> GetById(Guid classId, CancellationToken cancellationToken)
    {
        var admissionClass = await classService.GetByIdAsync(classId, cancellationToken);
        return admissionClass is null
            ? Problem(statusCode: StatusCodes.Status404NotFound, title: "Class not found.")
            : Ok(admissionClass);
    }
}
