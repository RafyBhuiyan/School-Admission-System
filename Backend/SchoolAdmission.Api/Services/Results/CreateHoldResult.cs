using SchoolAdmission.Api.DTOs.Responses;

namespace SchoolAdmission.Api.Services.Results;

public enum CreateHoldOutcome { Created, ClassNotFound, DuplicateApplication, ClassFull }

public sealed record CreateHoldResult(CreateHoldOutcome Outcome, CreateHoldResponse? Hold = null);
