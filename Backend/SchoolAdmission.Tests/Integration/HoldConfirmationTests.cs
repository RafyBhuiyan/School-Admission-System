using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text;
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

[Trait("Category", "PostgreSQLConfirmation")]
public sealed class HoldConfirmationTests(PostgreSqlSchemaFixture database)
    : IClassFixture<PostgreSqlSchemaFixture>, IAsyncLifetime
{
    private readonly string applicationName = $"confirmation-{Guid.NewGuid():N}";
    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PostgreSqlSchemaFixture.EnvironmentVariable))) return;
        await using var context = database.CreateContext();
        await context.AdmissionHolds.ExecuteDeleteAsync();
        await context.AdmissionClasses.ExecuteDeleteAsync();
    }
    public Task DisposeAsync() => Task.CompletedTask;

    [PostgreSqlSchemaFact]
    public async Task ConfirmationReturnsExactContractAndRetryPreservesTimestampAndOccupancy()
    {
        var hold = await SeedAsync();
        using var factory = Factory();
        using var client = factory.CreateClient();
        await using var context = database.CreateContext();
        var before = await TimeAsync(context);
        using var response = await ConfirmAsync(client, hold.Id, " Student-1 ", "{}");
        var after = await TimeAsync(context);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(new[] { "holdId", "status" }, body.RootElement.EnumerateObject().Select(property => property.Name).Order());
        Assert.Equal(new ConfirmHoldResponse(hold.Id, "CONFIRMED"), await response.Content.ReadFromJsonAsync<ConfirmHoldResponse>());
        var stored = await context.AdmissionHolds.AsNoTracking().SingleAsync();
        Assert.Equal(AdmissionHoldStatus.CONFIRMED, stored.Status);
        Assert.NotNull(stored.ConfirmedAt);
        Assert.InRange(stored.ConfirmedAt.Value, before, after);
        Assert.Equal((hold.CreatedAt, hold.ExpiresAt, hold.CancelledAt), (stored.CreatedAt, stored.ExpiresAt, stored.CancelledAt));
        using var repeat = await ConfirmAsync(client, hold.Id);
        Assert.Equal(HttpStatusCode.OK, repeat.StatusCode);
        Assert.Equal(await response.Content.ReadAsStringAsync(), await repeat.Content.ReadAsStringAsync());
        Assert.Equal(stored.ConfirmedAt, (await context.AdmissionHolds.AsNoTracking().SingleAsync()).ConfirmedAt);
        var summary = await client.GetFromJsonAsync<ClassSummaryResponse>($"/classes/{hold.ClassId}");
        Assert.Equal((0, 1, 0), (summary!.HeldSeats, summary.ConfirmedSeats, summary.AvailableSeats));
        Assert.Equal(1, await context.AdmissionHolds.CountAsync());
    }

    [PostgreSqlSchemaFact]
    public async Task AlreadyConfirmedWithHistoricalExpiryStillReturnsSuccess()
    {
        var hold = await SeedAsync(AdmissionHoldStatus.CONFIRMED, -60);
        using var factory = Factory();
        using var client = factory.CreateClient();
        using var response = await ConfirmAsync(client, hold.Id);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var context = database.CreateContext();
        Assert.Equal(hold.ConfirmedAt, (await context.AdmissionHolds.SingleAsync()).ConfirmedAt);
    }

    [PostgreSqlSchemaTheory]
    [InlineData(AdmissionHoldStatus.HELD)]
    [InlineData(AdmissionHoldStatus.EXPIRED)]
    [InlineData(AdmissionHoldStatus.CANCELLED)]
    public async Task IneligibleHoldReturns409AndOverdueExpiryIsCommitted(AdmissionHoldStatus status)
    {
        var hold = await SeedAsync(status, -60);
        using var factory = Factory();
        using var client = factory.CreateClient();
        using var response = await ConfirmAsync(client, hold.Id);
        await AssertProblemAsync(response, HttpStatusCode.Conflict);
        await using var context = database.CreateContext();
        var stored = await context.AdmissionHolds.SingleAsync();
        Assert.Equal(status == AdmissionHoldStatus.HELD ? AdmissionHoldStatus.EXPIRED : status, stored.Status);
        Assert.Null(stored.ConfirmedAt);
        Assert.Equal((hold.CreatedAt, hold.ExpiresAt, hold.CancelledAt), (stored.CreatedAt, stored.ExpiresAt, stored.CancelledAt));
    }

    [PostgreSqlSchemaTheory]
    [InlineData(AdmissionHoldStatus.HELD, 60)]
    [InlineData(AdmissionHoldStatus.HELD, -60)]
    [InlineData(AdmissionHoldStatus.CONFIRMED, -60)]
    [InlineData(AdmissionHoldStatus.EXPIRED, -60)]
    [InlineData(AdmissionHoldStatus.CANCELLED, -60)]
    public async Task OwnershipMismatchReturns403BeforeAnyStateChange(AdmissionHoldStatus status, int minutes)
    {
        var hold = await SeedAsync(status, minutes);
        using var factory = Factory();
        using var client = factory.CreateClient();
        using var response = await ConfirmAsync(client, hold.Id, "student-1"); // Case is significant.
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
    public async Task InvalidStudentHeaderReturns400(string? studentId)
    {
        var hold = await SeedAsync();
        using var factory = Factory();
        using var client = factory.CreateClient();
        using var response = await ConfirmAsync(client, hold.Id, studentId);
        await AssertProblemAsync(response, HttpStatusCode.BadRequest);
        await using var context = database.CreateContext();
        Assert.Equal(AdmissionHoldStatus.HELD, (await context.AdmissionHolds.SingleAsync()).Status);
    }

    [PostgreSqlSchemaFact]
    public async Task MultipleHeadersReturn400AndTrimmed64CharacterIdIsAccepted()
    {
        var hold = await SeedAsync(studentId: new string('a', 64));
        using var factory = Factory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/holds/{hold.Id}/confirm");
        request.Headers.TryAddWithoutValidation("X-Student-Id", new[] { "Student-1", "other" });
        using var invalid = await client.SendAsync(request);
        await AssertProblemAsync(invalid, HttpStatusCode.BadRequest);
        using var valid = await ConfirmAsync(client, hold.Id, $"  {new string('a', 64)}  ");
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
    }

    [PostgreSqlSchemaTheory]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("42")]
    [InlineData("\"payment\"")]
    [InlineData("{\"status\":\"CONFIRMED\"}")]
    public async Task InvalidBodyCannotChangeTheHold(string json)
    {
        var hold = await SeedAsync();
        using var factory = Factory();
        using var client = factory.CreateClient();
        using var response = await ConfirmAsync(client, hold.Id, json: json);
        await AssertProblemAsync(response, HttpStatusCode.BadRequest);
        await using var context = database.CreateContext();
        Assert.Equal(AdmissionHoldStatus.HELD, (await context.AdmissionHolds.SingleAsync()).Status);
    }

    [PostgreSqlSchemaFact]
    public async Task MissingAndMalformedIdsReturn404And400()
    {
        using var factory = Factory();
        using var client = factory.CreateClient();
        using var missing = await ConfirmAsync(client, Guid.NewGuid());
        await AssertProblemAsync(missing, HttpStatusCode.NotFound);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/holds/not-a-uuid/confirm");
        request.Headers.Add("X-Student-Id", "Student-1");
        using var malformed = await client.SendAsync(request);
        await AssertProblemAsync(malformed, HttpStatusCode.BadRequest);
    }

    [PostgreSqlSchemaTheory]
    [InlineData(-1, ConfirmHoldOutcome.Conflict)]
    [InlineData(0, ConfirmHoldOutcome.Conflict)]
    [InlineData(1, ConfirmHoldOutcome.Confirmed)]
    public async Task ExactExpiryBoundaryUsesPostLockTime(int offsetMicroseconds, ConfirmHoldOutcome expected)
    {
        var hold = await SeedAsync();
        DateTime now;
        await using (var seed = database.CreateContext())
        {
            now = await TimeAsync(seed);
            await seed.AdmissionHolds.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ExpiresAt, now.AddTicks(offsetMicroseconds * 10L)));
        }
        var clock = new FixedDatabaseTimeInterceptor(now);
        await using (var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(database.ConnectionString).AddInterceptors(clock).Options))
        {
            var result = await new HoldService(context, Options.Create(new HoldOptions())).ConfirmAsync(hold.Id, hold.StudentId, CancellationToken.None);
            Assert.Equal(expected, result.Outcome);
            Assert.Equal(1, clock.ClockReads);
        }
        await using var check = database.CreateContext();
        var stored = await check.AdmissionHolds.SingleAsync();
        Assert.Equal(offsetMicroseconds > 0 ? AdmissionHoldStatus.CONFIRMED : AdmissionHoldStatus.EXPIRED, stored.Status);
        Assert.Equal(offsetMicroseconds > 0 ? now : (DateTime?)null, stored.ConfirmedAt);
    }

    [PostgreSqlSchemaFact]
    public async Task EightQueuedConfirmationsReturnTheSameResultAndOneAdmission()
    {
        var hold = await SeedAsync();
        using var factory = Factory();
        using var client = factory.CreateClient();
        await using var blocker = database.CreateContext();
        await using var transaction = await blocker.Database.BeginTransactionAsync();
        await blocker.AdmissionHolds.FromSql($"SELECT * FROM \"AdmissionHolds\" WHERE id = {hold.Id} FOR UPDATE").ToListAsync();
        var pending = Enumerable.Range(0, 8).Select(_ => ConfirmAsync(client, hold.Id)).ToArray();
        await WaitForBlockedAsync(8);
        await transaction.CommitAsync();
        var responses = await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(15));
        try
        {
            foreach (var response in responses)
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal(new ConfirmHoldResponse(hold.Id, "CONFIRMED"), await response.Content.ReadFromJsonAsync<ConfirmHoldResponse>());
            }
            await using var check = database.CreateContext();
            Assert.Equal(1, await check.AdmissionHolds.CountAsync());
            Assert.NotNull((await check.AdmissionHolds.SingleAsync()).ConfirmedAt);
            var summary = await client.GetFromJsonAsync<ClassSummaryResponse>($"/classes/{hold.ClassId}");
            Assert.Equal((0, 1, 0), (summary!.HeldSeats, summary.ConfirmedSeats, summary.AvailableSeats));
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [PostgreSqlSchemaFact]
    public async Task ExpiryDuringLockWaitIsCommittedBefore409()
    {
        var hold = await SeedAsync();
        using var factory = Factory();
        using var client = factory.CreateClient();
        await using var blocker = database.CreateContext();
        await using var transaction = await blocker.Database.BeginTransactionAsync();
        await blocker.AdmissionHolds.FromSql($"SELECT * FROM \"AdmissionHolds\" WHERE id = {hold.Id} FOR UPDATE").ToListAsync();
        var pending = ConfirmAsync(client, hold.Id);
        await WaitForBlockedAsync(1);
        var expiry = await TimeAsync(blocker);
        await blocker.AdmissionHolds.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ExpiresAt, expiry));
        await transaction.CommitAsync();
        using var response = await pending.WaitAsync(TimeSpan.FromSeconds(15));
        await AssertProblemAsync(response, HttpStatusCode.Conflict);
        await using var check = database.CreateContext();
        var stored = await check.AdmissionHolds.SingleAsync();
        Assert.Equal(AdmissionHoldStatus.EXPIRED, stored.Status);
        Assert.Null(stored.ConfirmedAt);
        Assert.Equal(expiry, stored.ExpiresAt);
    }

    [PostgreSqlSchemaFact]
    public async Task ExpirationWorkerWinsLockAndConfirmationCannotResurrectHold()
    {
        var hold = await SeedAsync(minutes: -60);
        var gate = new PauseExpirationLockInterceptor();
        await using var workerContext = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(database.ConnectionString).AddInterceptors(gate).Options);
        var worker = new HoldExpirationService(workerContext, Options.Create(new ExpirationOptions())).ExpireBatchAsync(CancellationToken.None);
        using var factory = Factory();
        using var client = factory.CreateClient();
        try
        {
            await gate.Locked.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var pending = ConfirmAsync(client, hold.Id);
            await WaitForBlockedAsync(1);
            gate.Release.TrySetResult();
            Assert.Equal(1, await worker.WaitAsync(TimeSpan.FromSeconds(10)));
            using var response = await pending.WaitAsync(TimeSpan.FromSeconds(10));
            await AssertProblemAsync(response, HttpStatusCode.Conflict);
        }
        finally { gate.Release.TrySetResult(); }
        await using var check = database.CreateContext();
        Assert.Equal(AdmissionHoldStatus.EXPIRED, (await check.AdmissionHolds.SingleAsync()).Status);
    }

    [PostgreSqlSchemaFact]
    public async Task OpenApiDocumentsConfirmationAndRequiredHeader()
    {
        using var factory = Factory();
        using var client = factory.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        var operation = document.RootElement.GetProperty("paths").GetProperty("/holds/{holdId}/confirm").GetProperty("post");
        Assert.True(operation.GetProperty("parameters").EnumerateArray().Single(p => p.GetProperty("name").GetString() == "X-Student-Id").GetProperty("required").GetBoolean());
        foreach (var status in new[] { "200", "400", "403", "404", "409" })
            Assert.True(operation.GetProperty("responses").TryGetProperty(status, out _));
    }

    private FoundationApiFactory Factory() => new(overrides: new Dictionary<string, string?>
    {
        ["ConnectionStrings:DefaultConnection"] = new NpgsqlConnectionStringBuilder(database.ConnectionString)
            { ApplicationName = applicationName }.ConnectionString
    });
    private async Task<AdmissionHold> SeedAsync(AdmissionHoldStatus status = AdmissionHoldStatus.HELD, int minutes = 60, string studentId = "Student-1")
    {
        await using var context = database.CreateContext();
        var now = await TimeAsync(context);
        var admissionClass = new AdmissionClass { Name = "Confirmation test", TotalSeats = 1 };
        var hold = new AdmissionHold { AdmissionClass = admissionClass, ClassId = admissionClass.Id, StudentId = studentId,
            Status = status, ExpiresAt = now.AddMinutes(minutes),
            ConfirmedAt = status == AdmissionHoldStatus.CONFIRMED ? now.AddHours(-2) : null,
            CancelledAt = status == AdmissionHoldStatus.CANCELLED ? now.AddHours(-2) : null };
        context.AdmissionHolds.Add(hold);
        await context.SaveChangesAsync();
        return hold;
    }
    private static Task<DateTime> TimeAsync(AppDbContext context) => context.Database.SqlQuery<DateTime>($"SELECT clock_timestamp() AS \"Value\"").SingleAsync();
    private static async Task<HttpResponseMessage> ConfirmAsync(HttpClient client, Guid id, string? studentId = "Student-1", string? json = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/holds/{id}/confirm");
        if (studentId is not null) request.Headers.TryAddWithoutValidation("X-Student-Id", studentId);
        if (json is not null) request.Content = new StringContent(json, Encoding.UTF8, "application/json");
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
    private sealed class PauseExpirationLockInterceptor : DbCommandInterceptor
    {
        public TaskCompletionSource Locked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FOR UPDATE SKIP LOCKED", StringComparison.Ordinal))
            {
                Locked.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }
}
