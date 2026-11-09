namespace SchoolAdmission.Api.DTOs.Responses;

public sealed record HoldDetailsResponse(Guid HoldId, Guid ClassId, string Status, DateTime ExpiresAt, DateTime ServerTime);
