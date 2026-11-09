using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace SchoolAdmission.Tests.Integration;

// Only boundary tests freeze the clock input. Comparisons still execute on PostgreSQL;
// separate regression tests exercise the unmodified database wall clock.
internal sealed class FixedDatabaseTimeInterceptor(DateTime value) : DbCommandInterceptor
{
    public int ClockReads { get; private set; }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        const string clockExpression = "SELECT clock_timestamp() AS \"Value\"";
        Assert.Contains(clockExpression, command.CommandText);
        command.CommandText = command.CommandText.Replace(clockExpression,
            "SELECT @test_database_time AS \"Value\"", StringComparison.Ordinal);
        command.Parameters.Add(new NpgsqlParameter<DateTime>("test_database_time", value));
        ClockReads++;
        return ValueTask.FromResult(result);
    }
}
