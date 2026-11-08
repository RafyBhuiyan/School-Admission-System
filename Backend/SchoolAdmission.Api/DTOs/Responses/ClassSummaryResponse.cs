namespace SchoolAdmission.Api.DTOs.Responses;

public sealed record ClassSummaryResponse(
    Guid Id,
    string Name,
    int TotalSeats,
    int HeldSeats,
    int ConfirmedSeats,
    int AvailableSeats);
