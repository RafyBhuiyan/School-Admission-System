namespace SchoolAdmission.Api.Configuration;

public sealed class ExpirationOptions
{
    public int IntervalMilliseconds { get; set; } = 1000;
    public int BatchSize { get; set; } = 100;
}
