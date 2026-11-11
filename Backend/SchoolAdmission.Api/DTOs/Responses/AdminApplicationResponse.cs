namespace SchoolAdmission.Api.DTOs.Responses;

public sealed record AdminApplicationResponse(
    Guid HoldId,
    Guid ClassId,
    string ClassName,
    string StudentId,
    string Status,
    DateTime CreatedAt,
    DateTime ExpiresAt);
