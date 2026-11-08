using System.Text.Json.Serialization;

namespace SchoolAdmission.Api.DTOs.Requests;

// The student comes from the header and the class from the route. No body fields are needed.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CreateHoldRequest;
