namespace SchoolAdmission.Api.DTOs.Responses;

public sealed record CreateHoldResponse(Guid HoldId, Guid ClassId, string StudentId, string Status, DateTime ExpiresAt);
