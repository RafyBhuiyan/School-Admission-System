namespace SchoolAdmission.Api.Models.Entities;

public sealed class AdmissionClass
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public int TotalSeats { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<AdmissionHold> Holds { get; set; } = new List<AdmissionHold>();
}
