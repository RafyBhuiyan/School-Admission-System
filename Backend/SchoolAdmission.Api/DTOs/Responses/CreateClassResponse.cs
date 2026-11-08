namespace SchoolAdmission.Api.DTOs.Responses;

public sealed record CreateClassResponse(Guid Id, string Name, int TotalSeats);
