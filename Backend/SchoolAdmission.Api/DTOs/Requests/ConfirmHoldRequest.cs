using System.Text.Json.Serialization;

namespace SchoolAdmission.Api.DTOs.Requests;

// Payment is simulated; no payment data or client-supplied status is accepted.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ConfirmHoldRequest;
