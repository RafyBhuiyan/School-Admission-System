using SchoolAdmission.Api.Enums;

namespace SchoolAdmission.Api.Models.Entities;

public sealed class AdmissionHold
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClassId { get; set; }
    public required string StudentId { get; set; }
    public AdmissionHoldStatus Status { get; set; } = AdmissionHoldStatus.HELD;
    public DateTime CreatedAt { get; set; }
    public required DateTime ExpiresAt { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public DateTime? CancelledAt { get; set; }

    public AdmissionClass AdmissionClass { get; set; } = null!;
}
