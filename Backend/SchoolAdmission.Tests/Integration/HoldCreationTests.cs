using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SchoolAdmission.Api.Data;
using SchoolAdmission.Api.DTOs.Responses;
using SchoolAdmission.Api.Enums;
using SchoolAdmission.Api.Models.Entities;

namespace SchoolAdmission.Tests.Integration;

[Trait("Category", "PostgreSQLHoldCreation")]
public sealed class HoldCreationTests(PostgreSqlSchemaFixture database)
    : IClassFixture<PostgreSqlSchemaFixture>, IAsyncLifetime
{
    private readonly string applicationName = $"hold-creation-{Guid.NewGuid():N}";

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PostgreSqlSchemaFixture.EnvironmentVariable))) return;
        await using var context = database.CreateContext();
        await context.AdmissionHolds.ExecuteDeleteAsync();
        await context.AdmissionClasses.ExecuteDeleteAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [PostgreSqlSchemaTheory]
    [InlineData(null, 600)]
    [InlineData("37", 37)]
    [InlineData("1", 1)]
    public async Task CreateReturnsExactContractAndUsesConfiguredDatabaseClockDuration(string? setting, int duration)
    {
        var admissionClass = await CreateClassAsync(2);
        using var factory = CreateFactory(setting);
        using var client = factory.CreateClient();
        await using var context = database.CreateContext();
        var before = await ReadTimeAsync(context);

        using var response = await ApplyAsync(client, admissionClass.Id, "  000Student-1  ");

        var after = await ReadTimeAsync(context);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(new[] { "classId", "expiresAt", "holdId", "status", "studentId" },
            body.RootElement.EnumerateObject().Select(property => property.Name).Order());
        var created = (await response.Content.ReadFromJsonAsync<CreateHoldResponse>())!;
        Assert.NotEqual(Guid.Empty, created.HoldId);
        Assert.Equal(admissionClass.Id, created.ClassId);
        Assert.Equal("000Student-1", created.StudentId);
        Assert.Equal("HELD", created.Status);
        var stored = await context.AdmissionHolds.SingleAsync();
        Assert.Equal(created.HoldId, stored.Id);
        Assert.Equal(created.StudentId, stored.StudentId);
        Assert.InRange(stored.CreatedAt, before, after);
        Assert.Equal(stored.CreatedAt.AddSeconds(duration), created.ExpiresAt);
        Assert.Equal(created.ExpiresAt, stored.ExpiresAt);
        Assert.Null(stored.ConfirmedAt);
        Assert.Null(stored.CancelledAt);
    }

    [PostgreSqlSchemaTheory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx")]
    public async Task InvalidStudentHeaderReturns400WithoutWriting(string? studentId)
    {
        var admissionClass = await CreateClassAsync(1);
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var response = await ApplyAsync(client, admissionClass.Id, studentId);
        await AssertProblemAsync(response, HttpStatusCode.BadRequest);
        await using var context = database.CreateContext();
        Assert.Empty(await context.AdmissionHolds.ToListAsync());
    }

    [PostgreSqlSchemaFact]
    public async Task MultipleStudentHeadersReturn400()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/classes/{Guid.NewGuid()}/holds");
        request.Headers.TryAddWithoutValidation("X-Student-Id", new[] { "student-1", "student-2" });
        using var response = await client.SendAsync(request);
        await AssertProblemAsync(response, HttpStatusCode.BadRequest);
    }

    [PostgreSqlSchemaFact]
    public async Task Trimmed64CharacterStudentIdAndEmptyObjectBodyAreAccepted()
    {
        var admissionClass = await CreateClassAsync(1);
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var response = await ApplyAsync(client, admissionClass.Id, $"  {new string('a', 64)}  ", "{}");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(new string('a', 64), (await response.Content.ReadFromJsonAsync<CreateHoldResponse>())!.StudentId);
    }

    [PostgreSqlSchemaTheory]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("42")]
    [InlineData("\"student-1\"")]
    [InlineData("{\"studentId\":\"body-override\"}")]
    public async Task InvalidBodyReturns400WithoutWriting(string json)
    {
        var admissionClass = await CreateClassAsync(1);
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var response = await ApplyAsync(client, admissionClass.Id, "student-1", json);
        await AssertProblemAsync(response, HttpStatusCode.BadRequest);
        await using var context = database.CreateContext();
        Assert.Empty(await context.AdmissionHolds.ToListAsync());
    }

    [PostgreSqlSchemaFact]
    public async Task UnknownClassReturns404AndMalformedClassIdReturns400()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var missing = await ApplyAsync(client, Guid.NewGuid(), "student-1");
        await AssertProblemAsync(missing, HttpStatusCode.NotFound);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/classes/not-a-uuid/holds");
        request.Headers.Add("X-Student-Id", "student-1");
        using var malformed = await client.SendAsync(request);
        await AssertProblemAsync(malformed, HttpStatusCode.BadRequest);
    }

    [PostgreSqlSchemaTheory]
    [InlineData(AdmissionHoldStatus.HELD)]
    [InlineData(AdmissionHoldStatus.CONFIRMED)]
    public async Task ActiveOrConfirmedDuplicateReturns409EvenWhenCapacityRemains(AdmissionHoldStatus status)
    {
        var admissionClass = await CreateClassAsync(3);
        var previous = await SeedHoldAsync(admissionClass.Id, "student-1", status, status == AdmissionHoldStatus.HELD ? 60 : -60);
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var response = await ApplyAsync(client, admissionClass.Id, " student-1 ");
        await AssertProblemAsync(response, HttpStatusCode.Conflict);
        await using var context = database.CreateContext();
        Assert.Equal(previous.Id, (await context.AdmissionHolds.SingleAsync()).Id);
    }

    [PostgreSqlSchemaTheory]
    [InlineData(AdmissionHoldStatus.HELD)]
    [InlineData(AdmissionHoldStatus.EXPIRED)]
    [InlineData(AdmissionHoldStatus.CANCELLED)]
    public async Task ReapplicationRetainsHistoryAndExpiresStaleHeldRows(AdmissionHoldStatus oldStatus)
    {
        var admissionClass = await CreateClassAsync(1);
        var previous = await SeedHoldAsync(admissionClass.Id, "student-1", oldStatus, -60);
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var response = await ApplyAsync(client, admissionClass.Id, "student-1");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<CreateHoldResponse>())!;
        Assert.NotEqual(previous.Id, created.HoldId);
        await using var context = database.CreateContext();
        Assert.Equal(2, await context.AdmissionHolds.CountAsync());
        var historical = await context.AdmissionHolds.SingleAsync(hold => hold.Id == previous.Id);
        Assert.Equal(oldStatus == AdmissionHoldStatus.HELD ? AdmissionHoldStatus.EXPIRED : oldStatus, historical.Status);
        Assert.Equal(previous.ExpiresAt, historical.ExpiresAt);
        Assert.Equal(previous.CreatedAt, historical.CreatedAt);
    }

    [PostgreSqlSchemaTheory]
    [InlineData(AdmissionHoldStatus.HELD)]
    [InlineData(AdmissionHoldStatus.CONFIRMED)]
    public async Task OccupiedLastSeatReturns409ForAnotherStudent(AdmissionHoldStatus status)
    {
        var admissionClass = await CreateClassAsync(1);
        await SeedHoldAsync(admissionClass.Id, "student-1", status, status == AdmissionHoldStatus.HELD ? 60 : -60);
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var response = await ApplyAsync(client, admissionClass.Id, "student-2");
        await AssertProblemAsync(response, HttpStatusCode.Conflict);
        await using var context = database.CreateContext();
        Assert.Equal(1, await context.AdmissionHolds.CountAsync());
    }

    [PostgreSqlSchemaFact]
    public async Task StudentCanApplyToDifferentClassesAndIdentifierCaseIsPreserved()
    {
        var first = await CreateClassAsync(2, "First");
        var second = await CreateClassAsync(1, "Second");
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var one = await ApplyAsync(client, first.Id, "Student-1");
        using var two = await ApplyAsync(client, second.Id, "Student-1");
        using var differentCase = await ApplyAsync(client, first.Id, "student-1");
        Assert.Equal(HttpStatusCode.Created, one.StatusCode);
        Assert.Equal(HttpStatusCode.Created, two.StatusCode);
        Assert.Equal(HttpStatusCode.Created, differentCase.StatusCode);
    }

    [PostgreSqlSchemaFact]
    public async Task CleanupOnlyExpiresOverdueHeldRowsInTheLockedClass()
    {
        var target = await CreateClassAsync(3, "Target");
        var other = await CreateClassAsync(1, "Other");
        var overdue = await SeedHoldAsync(target.Id, "old-student", AdmissionHoldStatus.HELD, -60);
        var confirmed = await SeedHoldAsync(target.Id, "confirmed-student", AdmissionHoldStatus.CONFIRMED, -60);
        var active = await SeedHoldAsync(target.Id, "active-student", AdmissionHoldStatus.HELD, 60);
        var unrelated = await SeedHoldAsync(other.Id, "other-student", AdmissionHoldStatus.HELD, -60);
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var response = await ApplyAsync(client, target.Id, "new-student");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await using var context = database.CreateContext();
        var rows = await context.AdmissionHolds.ToDictionaryAsync(hold => hold.Id);
        Assert.Equal(AdmissionHoldStatus.EXPIRED, rows[overdue.Id].Status);
        Assert.Equal(AdmissionHoldStatus.CONFIRMED, rows[confirmed.Id].Status);
        Assert.Equal(AdmissionHoldStatus.HELD, rows[active.Id].Status);
        Assert.Equal(AdmissionHoldStatus.HELD, rows[unrelated.Id].Status);
    }

    [PostgreSqlSchemaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedAllocationRollsBackItsExpiryCleanup(bool duplicate)
    {
        var admissionClass = await CreateClassAsync(1);
        var overdue = await SeedHoldAsync(admissionClass.Id, "overdue-student", AdmissionHoldStatus.HELD, -60);
        await SeedHoldAsync(admissionClass.Id, "confirmed-student", AdmissionHoldStatus.CONFIRMED, -60);
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var response = await ApplyAsync(client, admissionClass.Id, duplicate ? "confirmed-student" : "new-student");
        await AssertProblemAsync(response, HttpStatusCode.Conflict);
        await using var context = database.CreateContext();
        Assert.Equal(2, await context.AdmissionHolds.CountAsync());
        Assert.Equal(AdmissionHoldStatus.HELD, (await context.AdmissionHolds.SingleAsync(hold => hold.Id == overdue.Id)).Status);
    }

    [PostgreSqlSchemaFact]
    public async Task TwoIndependentHostsCompetingForTheLastSeatCreateExactlyOneHold()
    {
        var admissionClass = await CreateClassAsync(1);
        using var firstFactory = CreateFactory();
        using var secondFactory = CreateFactory();
        using var firstClient = firstFactory.CreateClient();
        using var secondClient = secondFactory.CreateClient();
        await using var blocker = database.CreateContext();
        await using var transaction = await blocker.Database.BeginTransactionAsync();
        await LockClassAsync(blocker, admissionClass.Id);

        var first = ApplyAsync(firstClient, admissionClass.Id, "student-1");
        var second = ApplyAsync(secondClient, admissionClass.Id, "student-2");
        await WaitForBlockedRequestsAsync(2);
        await transaction.CommitAsync();
        var responses = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(15));
        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
            await AssertOccupiedSeatsAsync(admissionClass.Id, 1);
            var summary = await firstClient.GetFromJsonAsync<ClassSummaryResponse>($"/classes/{admissionClass.Id}");
            Assert.Equal(1, summary!.HeldSeats);
            Assert.Equal(0, summary.AvailableSeats);
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [PostgreSqlSchemaFact]
    public async Task EightQueuedApplicationsForTheSameStudentCreateExactlyOneHold()
    {
        var admissionClass = await CreateClassAsync(10);
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await using var blocker = database.CreateContext();
        await using var transaction = await blocker.Database.BeginTransactionAsync();
        await LockClassAsync(blocker, admissionClass.Id);
        var pending = Enumerable.Range(0, 8).Select(_ => ApplyAsync(client, admissionClass.Id, "student-1")).ToArray();
        await WaitForBlockedRequestsAsync(8);
        await transaction.CommitAsync();
        var responses = await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(15));
        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
            Assert.Equal(7, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
            await AssertOccupiedSeatsAsync(admissionClass.Id, 1);
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [PostgreSqlSchemaFact]
    public async Task LockWaitUsesFreshTimeForCleanupAndTheFullNewHoldDuration()
    {
        var admissionClass = await CreateClassAsync(1);
        var previous = await SeedHoldAsync(admissionClass.Id, "student-1", AdmissionHoldStatus.HELD, 60);
        using var factory = CreateFactory("37");
        using var client = factory.CreateClient();
        await using var blocker = database.CreateContext();
        await using var transaction = await blocker.Database.BeginTransactionAsync();
        await LockClassAsync(blocker, admissionClass.Id);
        var pending = ApplyAsync(client, admissionClass.Id, "student-1");
        await WaitForBlockedRequestsAsync(1);

        // Make the old hold expire after the request's transaction has begun, while
        // it is verifiably waiting for the class lock. No wall-clock sleeps are needed.
        var expiryWhileWaiting = await ReadTimeAsync(blocker);
        await blocker.AdmissionHolds.Where(hold => hold.Id == previous.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(hold => hold.ExpiresAt, expiryWhileWaiting));
        await transaction.CommitAsync();
        using var response = await pending.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await using var context = database.CreateContext();
        var old = await context.AdmissionHolds.SingleAsync(hold => hold.Id == previous.Id);
        var created = await context.AdmissionHolds.SingleAsync(hold => hold.Id != previous.Id);
        Assert.Equal(AdmissionHoldStatus.EXPIRED, old.Status);
        Assert.True(created.CreatedAt >= expiryWhileWaiting);
        Assert.Equal(created.CreatedAt.AddSeconds(37), created.ExpiresAt);
    }

    [PostgreSqlSchemaFact]
    public async Task OpenApiDocumentsHoldCreationAndItsRequiredStudentHeader()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        var operation = document.RootElement.GetProperty("paths").GetProperty("/classes/{classId}/holds").GetProperty("post");
        var header = operation.GetProperty("parameters").EnumerateArray().Single(parameter => parameter.GetProperty("name").GetString() == "X-Student-Id");
        Assert.Equal("header", header.GetProperty("in").GetString());
        Assert.True(header.GetProperty("required").GetBoolean());
        Assert.True(operation.GetProperty("responses").TryGetProperty("201", out _));
    }

    private FoundationApiFactory CreateFactory(string? duration = "600") => new(overrides: new Dictionary<string, string?>
    {
        ["ConnectionStrings:DefaultConnection"] = new NpgsqlConnectionStringBuilder(database.ConnectionString)
            { ApplicationName = applicationName }.ConnectionString,
        ["HOLD_DURATION_SECONDS"] = duration
    });

    private async Task<AdmissionClass> CreateClassAsync(int seats, string name = "Test class")
    {
        await using var context = database.CreateContext();
        var admissionClass = new AdmissionClass { Name = name, TotalSeats = seats };
        context.AdmissionClasses.Add(admissionClass);
        await context.SaveChangesAsync();
        return admissionClass;
    }

    private async Task<AdmissionHold> SeedHoldAsync(Guid classId, string studentId, AdmissionHoldStatus status, int expiryMinutes)
    {
        await using var context = database.CreateContext();
        var hold = new AdmissionHold { ClassId = classId, StudentId = studentId, Status = status,
            ExpiresAt = (await ReadTimeAsync(context)).AddMinutes(expiryMinutes) };
        context.AdmissionHolds.Add(hold);
        await context.SaveChangesAsync();
        return hold;
    }

    private static async Task<HttpResponseMessage> ApplyAsync(HttpClient client, Guid classId, string? studentId, string? json = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/classes/{classId}/holds");
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
        Assert.DoesNotContain("ux_admission_holds", body.RootElement.GetRawText());
    }

    private static Task<DateTime> ReadTimeAsync(AppDbContext context) =>
        context.Database.SqlQuery<DateTime>($"SELECT clock_timestamp() AS \"Value\"").SingleAsync();

    private static Task<List<AdmissionClass>> LockClassAsync(AppDbContext context, Guid classId) =>
        context.AdmissionClasses.FromSql($"SELECT * FROM \"AdmissionClasses\" WHERE id = {classId} FOR UPDATE").ToListAsync();

    private async Task WaitForBlockedRequestsAsync(int expected)
    {
        await using var context = database.CreateContext();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (true)
        {
            var blocked = await context.Database.SqlQuery<int>($"""
                SELECT count(*)::int AS "Value" FROM pg_stat_activity
                WHERE datname = current_database() AND application_name = {applicationName}
                    AND cardinality(pg_blocking_pids(pid)) > 0
                """).SingleAsync(timeout.Token);
            if (blocked == expected) return;
            await Task.Delay(20, timeout.Token);
        }
    }

    private async Task AssertOccupiedSeatsAsync(Guid classId, int expected)
    {
        await using var context = database.CreateContext();
        var now = await ReadTimeAsync(context);
        var occupied = await context.AdmissionHolds.CountAsync(hold => hold.ClassId == classId &&
            (hold.Status == AdmissionHoldStatus.CONFIRMED || (hold.Status == AdmissionHoldStatus.HELD && hold.ExpiresAt > now)));
        Assert.Equal(expected, occupied);
        Assert.Equal(expected, await context.AdmissionHolds.CountAsync(hold => hold.ClassId == classId));
        Assert.True(occupied <= (await context.AdmissionClasses.SingleAsync(admissionClass => admissionClass.Id == classId)).TotalSeats);
    }
}
