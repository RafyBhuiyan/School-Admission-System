using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Npgsql;
using SchoolAdmission.Api.Configuration;
using SchoolAdmission.Api.Data;
using SchoolAdmission.Api.DTOs.Responses;
using SchoolAdmission.Api.Enums;
using SchoolAdmission.Api.Models.Entities;
using SchoolAdmission.Api.Services;
using SchoolAdmission.Api.Services.Results;

namespace SchoolAdmission.Tests.Integration;

[Trait("Category", "PostgreSQLCancellation")]
public sealed class HoldCancellationTests(PostgreSqlSchemaFixture database)
    : IClassFixture<PostgreSqlSchemaFixture>, IAsyncLifetime
{
    private readonly string applicationName = $"cancellation-{Guid.NewGuid():N}";
    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PostgreSqlSchemaFixture.EnvironmentVariable))) return;
        await using var context = database.CreateContext();
        await context.AdmissionHolds.ExecuteDeleteAsync();
        await context.AdmissionClasses.ExecuteDeleteAsync();
    }
    public Task DisposeAsync() => Task.CompletedTask;

    [PostgreSqlSchemaTheory]
    [InlineData("Student-1")]
    [InlineData("student-2")]
    public async Task CancellationRetainsHistoryAndReleasesTheSeatForReapplication(string nextStudent)
    {
        var hold = await SeedAsync();
        using var factory = Factory();
        using var client = factory.CreateClient();
        await using var context = database.CreateContext();
        var before = await TimeAsync(context);
        using var response = await SendAsync(client, HttpMethod.Delete, hold.Id, " Student-1 ");
        var after = await TimeAsync(context);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("", await response.Content.ReadAsStringAsync());
        var stored = await context.AdmissionHolds.AsNoTracking().SingleAsync();
        Assert.Equal(AdmissionHoldStatus.CANCELLED, stored.Status);
        Assert.NotNull(stored.CancelledAt);
        Assert.InRange(stored.CancelledAt.Value, before, after);
        Assert.Equal((hold.CreatedAt, hold.ExpiresAt, hold.ConfirmedAt), (stored.CreatedAt, stored.ExpiresAt, stored.ConfirmedAt));
        using var repeat = await SendAsync(client, HttpMethod.Delete, hold.Id);
        Assert.Equal(HttpStatusCode.NoContent, repeat.StatusCode);
        Assert.Equal(stored.CancelledAt, (await context.AdmissionHolds.AsNoTracking().SingleAsync()).CancelledAt);
        var details = await client.GetFromJsonAsync<HoldDetailsResponse>($"/holds/{hold.Id}");
        Assert.Equal("CANCELLED", details!.Status);
        var summary = await client.GetFromJsonAsync<ClassSummaryResponse>($"/classes/{hold.ClassId}");
        Assert.Equal((0, 0, 1), (summary!.HeldSeats, summary.ConfirmedSeats, summary.AvailableSeats));
        using var apply = new HttpRequestMessage(HttpMethod.Post, $"/classes/{hold.ClassId}/holds");
        apply.Headers.Add("X-Student-Id", nextStudent);
        using var createdResponse = await client.SendAsync(apply);
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var created = (await createdResponse.Content.ReadFromJsonAsync<CreateHoldResponse>())!;
        Assert.NotEqual(hold.Id, created.HoldId);
        Assert.Equal(2, await context.AdmissionHolds.CountAsync());
        Assert.Equal(AdmissionHoldStatus.CANCELLED, (await context.AdmissionHolds.AsNoTracking().SingleAsync(row => row.Id == hold.Id)).Status);
    }

    [PostgreSqlSchemaFact]
    public async Task AlreadyCancelledSucceedsEvenAfterHistoricalExpiryWithoutChangingTimestamp()
    {
        var hold = await SeedAsync(AdmissionHoldStatus.CANCELLED, -60);
        using var factory = Factory();
        using var client = factory.CreateClient();
        using var response = await SendAsync(client, HttpMethod.Delete, hold.Id);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await using var context = database.CreateContext();
        Assert.Equal(hold.CancelledAt, (await context.AdmissionHolds.SingleAsync()).CancelledAt);
    }

    [PostgreSqlSchemaTheory]
    [InlineData(AdmissionHoldStatus.HELD)]
    [InlineData(AdmissionHoldStatus.CONFIRMED)]
    [InlineData(AdmissionHoldStatus.EXPIRED)]
    public async Task IneligibleHoldReturns409AndOverdueExpiryIsCommitted(AdmissionHoldStatus status)
    {
        var hold = await SeedAsync(status, -60);
        using var factory = Factory();
        using var client = factory.CreateClient();
        using var response = await SendAsync(client, HttpMethod.Delete, hold.Id);
        await AssertProblemAsync(response, HttpStatusCode.Conflict);
        await using var context = database.CreateContext();
        var stored = await context.AdmissionHolds.SingleAsync();
        Assert.Equal(status == AdmissionHoldStatus.HELD ? AdmissionHoldStatus.EXPIRED : status, stored.Status);
        Assert.Null(stored.CancelledAt);
        Assert.Equal((hold.CreatedAt, hold.ExpiresAt, hold.ConfirmedAt), (stored.CreatedAt, stored.ExpiresAt, stored.ConfirmedAt));
    }

    [PostgreSqlSchemaTheory]
    [InlineData(AdmissionHoldStatus.HELD, 60)]
    [InlineData(AdmissionHoldStatus.HELD, -60)]
    [InlineData(AdmissionHoldStatus.CONFIRMED, -60)]
    [InlineData(AdmissionHoldStatus.EXPIRED, -60)]
    [InlineData(AdmissionHoldStatus.CANCELLED, -60)]
    public async Task WrongOwnerAlwaysGets403WithoutMutation(AdmissionHoldStatus status, int minutes)
    {
        var hold = await SeedAsync(status, minutes);
        using var factory = Factory();
        using var client = factory.CreateClient();
        using var response = await SendAsync(client, HttpMethod.Delete, hold.Id, "student-1");
        await AssertProblemAsync(response, HttpStatusCode.Forbidden);
        await using var context = database.CreateContext();
        var stored = await context.AdmissionHolds.SingleAsync();
        Assert.Equal((hold.Status, hold.ExpiresAt, hold.ConfirmedAt, hold.CancelledAt),
            (stored.Status, stored.ExpiresAt, stored.ConfirmedAt, stored.CancelledAt));
    }

    [PostgreSqlSchemaTheory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx")]
    public async Task InvalidHeaderCannotCancel(string? studentId)
    {
        var hold = await SeedAsync();
        using var factory = Factory();
        using var client = factory.CreateClient();
        using var response = await SendAsync(client, HttpMethod.Delete, hold.Id, studentId);
        await AssertProblemAsync(response, HttpStatusCode.BadRequest);
        await using var context = database.CreateContext();
        Assert.Equal(AdmissionHoldStatus.HELD, (await context.AdmissionHolds.SingleAsync()).Status);
    }

    [PostgreSqlSchemaFact]
    public async Task MultipleHeadersAreRejectedAndTrimmed64CharacterOwnerIsAccepted()
    {
        var hold = await SeedAsync(studentId: new string('a', 64));
        using var factory = Factory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/holds/{hold.Id}");
        request.Headers.TryAddWithoutValidation("X-Student-Id", new[] { "one", "two" });
        using var invalid = await client.SendAsync(request);
        await AssertProblemAsync(invalid, HttpStatusCode.BadRequest);
        using var valid = await SendAsync(client, HttpMethod.Delete, hold.Id, $" {new string('a', 64)} ");
        Assert.Equal(HttpStatusCode.NoContent, valid.StatusCode);
    }

    [PostgreSqlSchemaFact]
    public async Task UnknownAndMalformedIdsReturn404And400()
    {
        using var factory = Factory();
        using var client = factory.CreateClient();
        using var missing = await SendAsync(client, HttpMethod.Delete, Guid.NewGuid());
        await AssertProblemAsync(missing, HttpStatusCode.NotFound);
        using var request = new HttpRequestMessage(HttpMethod.Delete, "/holds/not-a-uuid");
        request.Headers.Add("X-Student-Id", "Student-1");
        using var malformed = await client.SendAsync(request);
        await AssertProblemAsync(malformed, HttpStatusCode.BadRequest);
    }

    [PostgreSqlSchemaTheory]
    [InlineData(-1, CancelHoldOutcome.Conflict)]
    [InlineData(0, CancelHoldOutcome.Conflict)]
    [InlineData(1, CancelHoldOutcome.Cancelled)]
    public async Task ExactExpiryBoundaryPersistsTheCorrectTerminalState(int microseconds, CancelHoldOutcome expected)
    {
        var hold = await SeedAsync();
        DateTime now;
        await using (var seed = database.CreateContext())
        {
            now = await TimeAsync(seed);
            await seed.AdmissionHolds.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ExpiresAt, now.AddTicks(microseconds * 10L)));
        }
        var clock = new FixedDatabaseTimeInterceptor(now);
        await using (var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(database.ConnectionString).AddInterceptors(clock).Options))
        {
            Assert.Equal(expected, await Service(context).CancelAsync(hold.Id, hold.StudentId, CancellationToken.None));
            Assert.Equal(1, clock.ClockReads);
        }
        await using var check = database.CreateContext();
        var stored = await check.AdmissionHolds.SingleAsync();
        Assert.Equal(microseconds > 0 ? AdmissionHoldStatus.CANCELLED : AdmissionHoldStatus.EXPIRED, stored.Status);
        Assert.Equal(microseconds > 0 ? now : (DateTime?)null, stored.CancelledAt);
        Assert.Null(stored.ConfirmedAt);
    }

    [PostgreSqlSchemaFact]
    public async Task EightQueuedCancellationsReturn204AndKeepOneHistoryRow()
    {
        var hold = await SeedAsync();
        using var factory = Factory();
        using var client = factory.CreateClient();
        await using var blocker = database.CreateContext();
        await using var transaction = await blocker.Database.BeginTransactionAsync();
        await LockAsync(blocker, hold.Id);
        var pending = Enumerable.Range(0, 8).Select(_ => SendAsync(client, HttpMethod.Delete, hold.Id)).ToArray();
        await WaitForBlockedAsync(8);
        await transaction.CommitAsync();
        var responses = await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(15));
        try { foreach (var response in responses) Assert.Equal(HttpStatusCode.NoContent, response.StatusCode); }
        finally { foreach (var response in responses) response.Dispose(); }
        await using var context = database.CreateContext();
        var stored = await context.AdmissionHolds.SingleAsync();
        Assert.Equal(AdmissionHoldStatus.CANCELLED, stored.Status);
        Assert.NotNull(stored.CancelledAt);
        Assert.Null(stored.ConfirmedAt);
    }

    [PostgreSqlSchemaFact]
    public async Task ExpiryDuringLockWaitIsPersistedAsExpiredAndReturns409()
    {
        var hold = await SeedAsync();
        using var factory = Factory();
        using var client = factory.CreateClient();
        await using var blocker = database.CreateContext();
        await using var transaction = await blocker.Database.BeginTransactionAsync();
        await LockAsync(blocker, hold.Id);
        var pending = SendAsync(client, HttpMethod.Delete, hold.Id);
        await WaitForBlockedAsync(1);
        var expiry = await TimeAsync(blocker);
        await blocker.AdmissionHolds.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ExpiresAt, expiry));
        await transaction.CommitAsync();
        using var response = await pending.WaitAsync(TimeSpan.FromSeconds(15));
        await AssertProblemAsync(response, HttpStatusCode.Conflict);
        await using var context = database.CreateContext();
        var stored = await context.AdmissionHolds.SingleAsync();
        Assert.Equal(AdmissionHoldStatus.EXPIRED, stored.Status);
        Assert.Null(stored.CancelledAt);
        Assert.Equal(expiry, stored.ExpiresAt);
    }

    [PostgreSqlSchemaTheory]
    [InlineData("confirm", AdmissionHoldStatus.CONFIRMED)]
    [InlineData("cancel", AdmissionHoldStatus.CANCELLED)]
    [InlineData("expire", AdmissionHoldStatus.EXPIRED)]
    public async Task FirstMutationToLockTheHoldCannotBeOverwritten(string winner, AdmissionHoldStatus expected)
    {
        var hold = await SeedAsync(minutes: winner == "expire" ? -60 : 60);
        var gate = new PauseRowLockInterceptor();
        await using var winnerContext = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(database.ConnectionString).AddInterceptors(gate).Options);
        async Task RunWinnerAsync()
        {
            if (winner == "confirm") Assert.Equal(ConfirmHoldOutcome.Confirmed,
                (await Service(winnerContext).ConfirmAsync(hold.Id, hold.StudentId, CancellationToken.None)).Outcome);
            else if (winner == "cancel") Assert.Equal(CancelHoldOutcome.Cancelled,
                await Service(winnerContext).CancelAsync(hold.Id, hold.StudentId, CancellationToken.None));
            else Assert.Equal(1, await new HoldExpirationService(winnerContext, Options.Create(new ExpirationOptions())).ExpireBatchAsync(CancellationToken.None));
        }
        var first = RunWinnerAsync();
        using var factory = Factory();
        using var client = factory.CreateClient();
        try
        {
            await gate.Locked.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var pending = SendAsync(client, winner == "cancel" ? HttpMethod.Post : HttpMethod.Delete, hold.Id);
            await WaitForBlockedAsync(1);
            gate.Release.TrySetResult();
            await first.WaitAsync(TimeSpan.FromSeconds(10));
            using var response = await pending.WaitAsync(TimeSpan.FromSeconds(10));
            await AssertProblemAsync(response, HttpStatusCode.Conflict);
        }
        finally { gate.Release.TrySetResult(); }
        await using var check = database.CreateContext();
        var stored = await check.AdmissionHolds.SingleAsync();
        Assert.Equal(expected, stored.Status);
        Assert.Equal(expected == AdmissionHoldStatus.CONFIRMED, stored.ConfirmedAt.HasValue);
        Assert.Equal(expected == AdmissionHoldStatus.CANCELLED, stored.CancelledAt.HasValue);
    }

    [PostgreSqlSchemaFact]
    public async Task OpenApiDocumentsDeleteAndRequiredHeader()
    {
        using var factory = Factory();
        using var client = factory.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        var operation = document.RootElement.GetProperty("paths").GetProperty("/holds/{holdId}").GetProperty("delete");
        Assert.True(operation.GetProperty("parameters").EnumerateArray().Single(p => p.GetProperty("name").GetString() == "X-Student-Id").GetProperty("required").GetBoolean());
        foreach (var status in new[] { "204", "400", "403", "404", "409" }) Assert.True(operation.GetProperty("responses").TryGetProperty(status, out _));
    }

    private FoundationApiFactory Factory() => new(overrides: new Dictionary<string, string?>
    {
        ["ConnectionStrings:DefaultConnection"] = new NpgsqlConnectionStringBuilder(database.ConnectionString) { ApplicationName = applicationName }.ConnectionString
    });
    private static HoldService Service(AppDbContext context) => new(context, Options.Create(new HoldOptions()));
    private async Task<AdmissionHold> SeedAsync(AdmissionHoldStatus status = AdmissionHoldStatus.HELD, int minutes = 60, string studentId = "Student-1")
    {
        await using var context = database.CreateContext();
        var now = await TimeAsync(context);
        var admissionClass = new AdmissionClass { Name = "Cancellation test", TotalSeats = 1 };
        var hold = new AdmissionHold { AdmissionClass = admissionClass, ClassId = admissionClass.Id, StudentId = studentId,
            Status = status, ExpiresAt = now.AddMinutes(minutes), ConfirmedAt = status == AdmissionHoldStatus.CONFIRMED ? now.AddHours(-2) : null,
            CancelledAt = status == AdmissionHoldStatus.CANCELLED ? now.AddHours(-2) : null };
        context.AdmissionHolds.Add(hold);
        await context.SaveChangesAsync();
        return hold;
    }
    private static Task<DateTime> TimeAsync(AppDbContext context) => context.Database.SqlQuery<DateTime>($"SELECT clock_timestamp() AS \"Value\"").SingleAsync();
    private static Task<List<AdmissionHold>> LockAsync(AppDbContext context, Guid id) => context.AdmissionHolds
        .FromSql($"SELECT * FROM \"AdmissionHolds\" WHERE id = {id} FOR UPDATE").ToListAsync();
    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, Guid id, string? studentId = "Student-1")
    {
        using var request = new HttpRequestMessage(method, $"/holds/{id}{(method == HttpMethod.Post ? "/confirm" : "")}");
        if (studentId is not null) request.Headers.TryAddWithoutValidation("X-Student-Id", studentId);
        return await client.SendAsync(request);
    }
    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal((int)expected, body.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("traceId").GetString()));
    }
    private async Task WaitForBlockedAsync(int count)
    {
        await using var context = database.CreateContext();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (await context.Database.SqlQuery<int>($"""
            SELECT count(*)::int AS "Value" FROM pg_stat_activity WHERE datname = current_database()
                AND application_name = {applicationName} AND cardinality(pg_blocking_pids(pid)) > 0
            """).SingleAsync(timeout.Token) != count) await Task.Delay(20, timeout.Token);
    }
    private sealed class PauseRowLockInterceptor : DbCommandInterceptor
    {
        public TaskCompletionSource Locked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal))
            {
                Locked.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }
}
