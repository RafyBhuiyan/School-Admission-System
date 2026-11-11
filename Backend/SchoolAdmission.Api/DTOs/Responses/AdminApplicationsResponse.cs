namespace SchoolAdmission.Api.DTOs.Responses;

public sealed record AdminApplicationsResponse(
    List<AdminApplicationResponse> Items,
    int Total,
    int Page,
    int PageSize);
