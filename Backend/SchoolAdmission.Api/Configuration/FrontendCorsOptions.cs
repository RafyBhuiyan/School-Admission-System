namespace SchoolAdmission.Api.Configuration;

public sealed class FrontendCorsOptions
{
    public const string SectionName = "Cors";
    public const string PolicyName = "Frontend";

    public string[] AllowedOrigins { get; set; } = [];

    public bool IsValid() => AllowedOrigins is { Length: > 0 }
        && AllowedOrigins.All(origin =>
            Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https"
            && string.IsNullOrEmpty(uri.UserInfo)
            && !origin.Contains('*')
            && string.Equals(origin, uri.GetLeftPart(UriPartial.Authority), StringComparison.Ordinal));
}
