using Microsoft.EntityFrameworkCore;
using Npgsql;
using SchoolAdmission.Api.Data;

namespace SchoolAdmission.Tests.Integration;

public sealed class PostgreSqlSchemaFixture : IAsyncLifetime
{
    public const string EnvironmentVariable = "SCHEMA_TEST_CONNECTION_STRING";
    private readonly string databaseName = $"school_admission_schema_{Guid.NewGuid():N}";
    private string? adminConnectionString;
    private string? testConnectionString;
    private bool databaseCreated;

    public async Task InitializeAsync()
    {
        var configured = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (string.IsNullOrWhiteSpace(configured))
        {
            return;
        }

        var connection = new NpgsqlConnectionStringBuilder(configured) { Pooling = false };
        adminConnectionString = connection.ConnectionString;
        await using (var admin = new NpgsqlConnection(adminConnectionString))
        {
            await admin.OpenAsync();
            // The identifier is generated internally, never taken from configuration/input.
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", admin);
            await command.ExecuteNonQueryAsync();
            databaseCreated = true;
        }

        connection.Database = databaseName;
        testConnectionString = connection.ConnectionString;
        try
        {
            await using var context = CreateContext();
            await context.Database.MigrateAsync();
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    public AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(testConnectionString ?? throw new InvalidOperationException("Schema test database is not configured."))
        .Options);

    public async Task DisposeAsync()
    {
        if (!databaseCreated)
        {
            return;
        }

        await using var admin = new NpgsqlConnection(adminConnectionString);
        await admin.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE \"{databaseName}\" WITH (FORCE)", admin);
        await command.ExecuteNonQueryAsync();
        databaseCreated = false;
    }
}

public sealed class PostgreSqlSchemaFactAttribute : FactAttribute
{
    public PostgreSqlSchemaFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PostgreSqlSchemaFixture.EnvironmentVariable)))
        {
            Skip = "Set SCHEMA_TEST_CONNECTION_STRING to a PostgreSQL test account with CREATE DATABASE permission.";
        }
    }
}

public sealed class PostgreSqlSchemaTheoryAttribute : TheoryAttribute
{
    public PostgreSqlSchemaTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PostgreSqlSchemaFixture.EnvironmentVariable)))
        {
            Skip = "Set SCHEMA_TEST_CONNECTION_STRING to a PostgreSQL test account with CREATE DATABASE permission.";
        }
    }
}
