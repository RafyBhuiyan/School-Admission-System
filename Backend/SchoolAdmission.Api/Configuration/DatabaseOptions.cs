using Npgsql;

namespace SchoolAdmission.Api.Configuration;

public sealed class DatabaseOptions
{
    public const string SectionName = "ConnectionStrings";

    public string DefaultConnection { get; set; } = string.Empty;

    public bool IsValid()
    {
        try
        {
            var connection = new NpgsqlConnectionStringBuilder(DefaultConnection);
            return !string.IsNullOrWhiteSpace(connection.Host)
                && !string.IsNullOrWhiteSpace(connection.Database);
        }
        catch (ArgumentException)
        {
            // Never include a malformed connection string (or its password) in diagnostics.
            return false;
        }
    }
}
