namespace SchoolAdmission.Api.DTOs.Responses;

public sealed record StudentHoldResponse(Guid HoldId, Guid ClassId, string ClassName, string Status, DateTime ExpiresAt);
