namespace SchoolAdmission.Api.Configuration;

public sealed class HoldOptions
{
    public const string DurationKey = "HOLD_DURATION_SECONDS";
    public int DurationSeconds { get; set; } = 600;
}
