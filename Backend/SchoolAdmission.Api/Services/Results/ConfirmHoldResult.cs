using SchoolAdmission.Api.DTOs.Responses;

namespace SchoolAdmission.Api.Services.Results;

public enum ConfirmHoldOutcome { Confirmed, NotFound, Forbidden, Conflict }

public sealed record ConfirmHoldResult(ConfirmHoldOutcome Outcome, ConfirmHoldResponse? Hold = null);
