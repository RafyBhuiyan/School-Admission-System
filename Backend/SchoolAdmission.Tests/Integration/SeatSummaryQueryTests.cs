using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using SchoolAdmission.Api.Data;
using SchoolAdmission.Api.DTOs.Responses;
using SchoolAdmission.Api.Enums;
using SchoolAdmission.Api.Models.Entities;
using SchoolAdmission.Api.Services;

namespace SchoolAdmission.Tests.Integration;

[Trait("Category", "PostgreSQLSeatSummary")]
public sealed class SeatSummaryQueryTests(PostgreSqlSchemaFixture database)
    : IClassFixture<PostgreSqlSchemaFixture>, IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PostgreSqlSchemaFixture.EnvironmentVariable)))
        {
            return;
        }

        // Only this fixture's generated database is reset between cases.
        await using var context = database.CreateContext();
        await context.AdmissionHolds.ExecuteDeleteAsync();
        await context.AdmissionClasses.ExecuteDeleteAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [PostgreSqlSchemaTheory]
    [InlineData(AdmissionHoldStatus.HELD, -60, 0, 0)]
    [InlineData(AdmissionHoldStatus.HELD, 60, 1, 0)]
    [InlineData(AdmissionHoldStatus.CONFIRMED, -60, 0, 1)]
    [InlineData(AdmissionHoldStatus.CONFIRMED, 60, 0, 1)]
    [InlineData(AdmissionHoldStatus.EXPIRED, -60, 0, 0)]
    [InlineData(AdmissionHoldStatus.EXPIRED, 60, 0, 0)]
    [InlineData(AdmissionHoldStatus.CANCELLED, -60, 0, 0)]
    [InlineData(AdmissionHoldStatus.CANCELLED, 60, 0, 0)]
    public async Task StatusAndExpiryDetermineOccupancy(
        AdmissionHoldStatus status, int expiryMinutes, int expectedHeld, int expectedConfirmed)
    {
        var admissionClass = new AdmissionClass { Name = "Status matrix", TotalSeats = 3 };
        await using (var seed = database.CreateContext())
        {
            var now = await ReadDatabaseTimeAsync(seed);
            seed.AdmissionClasses.Add(admissionClass);
            seed.AdmissionHolds.Add(Hold(admissionClass, "student-1", status, now.AddMinutes(expiryMinutes)));
            await seed.SaveChangesAsync();
        }

        await using var context = database.CreateContext();
        await AssertSummariesAsync(context, Summary(admissionClass, expectedHeld, expectedConfirmed,
            3 - expectedHeld - expectedConfirmed));
    }

    [PostgreSqlSchemaTheory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    public async Task HeldExpiryBoundaryIsStrictAtPostgreSqlMicrosecondPrecision(int offsetMicroseconds, int expectedHeld)
    {
        var admissionClass = new AdmissionClass { Name = "Expiry boundary", TotalSeats = 1 };
        DateTime now;
        await using (var seed = database.CreateContext())
        {
            now = await ReadDatabaseTimeAsync(seed);
            seed.AdmissionClasses.Add(admissionClass);
            seed.AdmissionHolds.Add(Hold(admissionClass, "student-1", AdmissionHoldStatus.HELD,
                now.AddTicks(offsetMicroseconds * 10L)));
            await seed.SaveChangesAsync();
        }

        // Freeze only the clock input for exact equality; execute the actual LINQ-generated
        // comparison/counts on PostgreSQL. The separate transaction test uses the real clock.
        var clock = new FixedDatabaseTimeInterceptor(now);
        await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(database.ConnectionString).AddInterceptors(clock).Options);
        await AssertSummariesAsync(context, Summary(admissionClass, expectedHeld, 0, 1 - expectedHeld));
        Assert.Equal(2, clock.ClockReads);
    }

    [PostgreSqlSchemaTheory]
    [InlineData(1, 1)]
    [InlineData(3, 0)]
    [InlineData(0, 3)]
    [InlineData(2, 2)]
    public async Task FullAndOverCapacityDataReturnsZeroAvailabilityWithoutHidingCounts(int held, int confirmed)
    {
        var admissionClass = new AdmissionClass { Name = "Capacity boundary", TotalSeats = 2 };
        await using (var seed = database.CreateContext())
        {
            var now = await ReadDatabaseTimeAsync(seed);
            seed.AdmissionClasses.Add(admissionClass);
            for (var index = 0; index < held; index++)
            {
                seed.AdmissionHolds.Add(Hold(admissionClass, $"held-{index}", AdmissionHoldStatus.HELD, now.AddHours(1)));
            }

            for (var index = 0; index < confirmed; index++)
            {
                seed.AdmissionHolds.Add(Hold(admissionClass, $"confirmed-{index}", AdmissionHoldStatus.CONFIRMED, now.AddHours(-1)));
            }

            // Deliberately seed invalid occupancy to test the defensive response floor.
            // This does not exercise or authorize overbooking through the future hold API.
            await seed.SaveChangesAsync();
        }

        await using var context = database.CreateContext();
        await AssertSummariesAsync(context, Summary(admissionClass, held, confirmed, 0));
    }

    [PostgreSqlSchemaFact]
    public async Task CountsStayWithinEachClassAndIgnorePreviousApplicationsForTheSameStudent()
    {
        var morning = new AdmissionClass { Name = "Morning", TotalSeats = 4 };
        var day = new AdmissionClass { Name = "Day", TotalSeats = 3 };
        var empty = new AdmissionClass { Name = "Empty", TotalSeats = 2 };
        await using (var seed = database.CreateContext())
        {
            var now = await ReadDatabaseTimeAsync(seed);
            seed.AdmissionClasses.AddRange(morning, day, empty);
            seed.AdmissionHolds.AddRange(
                Hold(morning, "student-1", AdmissionHoldStatus.EXPIRED, now.AddHours(-2)),
                Hold(morning, "student-1", AdmissionHoldStatus.CANCELLED, now.AddHours(1)),
                Hold(morning, "student-1", AdmissionHoldStatus.HELD, now.AddHours(1)),
                Hold(morning, "student-2", AdmissionHoldStatus.HELD, now.AddHours(1)),
                Hold(day, "student-1", AdmissionHoldStatus.CONFIRMED, now.AddHours(-1)));
            await seed.SaveChangesAsync();
        }

        await using var context = database.CreateContext();
        await AssertSummariesAsync(context,
            Summary(morning, 2, 0, 2), Summary(day, 0, 1, 2), Summary(empty, 0, 0, 2));
    }

    [PostgreSqlSchemaFact]
    public async Task SummaryUsesFreshDatabaseTimeInsideAnExistingTransactionWithoutPersistingExpiry()
    {
        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var transactionStart = await context.Database.SqlQuery<DateTime>($"SELECT CURRENT_TIMESTAMP AS \"Value\"").SingleAsync();
        var expiresAt = await ReadDatabaseTimeAsync(context);
        Assert.True(expiresAt > transactionStart, "The expiry must follow transaction start to catch a regression to NOW().");

        var admissionClass = new AdmissionClass { Name = "Old transaction", TotalSeats = 1 };
        var hold = Hold(admissionClass, "student-1", AdmissionHoldStatus.HELD, expiresAt);
        context.AdmissionClasses.Add(admissionClass);
        context.AdmissionHolds.Add(hold);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        // The expiry is after transaction start but already past wall-clock time. NOW()
        // would incorrectly count it; clock_timestamp() must exclude it without a worker.
        await AssertSummariesAsync(context, Summary(admissionClass, 0, 0, 1));
        var stored = await context.AdmissionHolds.AsNoTracking().SingleAsync();
        Assert.Equal(AdmissionHoldStatus.HELD, stored.Status);
        Assert.Equal(expiresAt, stored.ExpiresAt);
        Assert.Null(stored.ConfirmedAt);
        Assert.Null(stored.CancelledAt);
        await transaction.RollbackAsync();
    }

    private static async Task AssertSummariesAsync(AppDbContext context, params ClassSummaryResponse[] expected)
    {
        var service = new ClassService(context);
        var summaries = await service.GetAllAsync(CancellationToken.None);
        Assert.Equal(expected.OrderBy(summary => summary.Name).ThenBy(summary => summary.Id), summaries);
        foreach (var summary in expected)
        {
            Assert.Equal(summary, await service.GetByIdAsync(summary.Id, CancellationToken.None));
        }

        Assert.Empty(context.ChangeTracker.Entries());
    }

    private static Task<DateTime> ReadDatabaseTimeAsync(AppDbContext context) =>
        context.Database.SqlQuery<DateTime>($"SELECT clock_timestamp() AS \"Value\"").SingleAsync();

    private static AdmissionHold Hold(AdmissionClass admissionClass, string studentId, AdmissionHoldStatus status, DateTime expiresAt) =>
        new() { ClassId = admissionClass.Id, StudentId = studentId, Status = status, ExpiresAt = expiresAt };

    private static ClassSummaryResponse Summary(AdmissionClass admissionClass, int held, int confirmed, int available) =>
        new(admissionClass.Id, admissionClass.Name, admissionClass.TotalSeats, held, confirmed, available);

    private sealed class FixedDatabaseTimeInterceptor(DateTime value) : DbCommandInterceptor
    {
        public int ClockReads { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            const string clockExpression = "SELECT clock_timestamp() AS \"Value\"";
            Assert.Contains(clockExpression, command.CommandText);
            command.CommandText = command.CommandText.Replace(clockExpression,
                "SELECT @seat_summary_test_time AS \"Value\"", StringComparison.Ordinal);
            command.Parameters.Add(new NpgsqlParameter<DateTime>("seat_summary_test_time", value));
            ClockReads++;
            return ValueTask.FromResult(result);
        }
    }
}
