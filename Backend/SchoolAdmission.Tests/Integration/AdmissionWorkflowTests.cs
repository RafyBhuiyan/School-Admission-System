using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolAdmission.Api.DTOs.Responses;
using SchoolAdmission.Api.Enums;

namespace SchoolAdmission.Tests.Integration;

[Trait("Category", "PostgreSQLWorkflow")]
public sealed class AdmissionWorkflowTests(PostgreSqlSchemaFixture database)
    : IClassFixture<PostgreSqlSchemaFixture>, IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        if (!Configured) return;
        await DropFailureTriggerAsync();
        await using var context = database.CreateContext();
        await context.AdmissionHolds.ExecuteDeleteAsync();
        await context.AdmissionClasses.ExecuteDeleteAsync();
    }
    public Task DisposeAsync() => Configured ? DropFailureTriggerAsync() : Task.CompletedTask;
    private static bool Configured => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PostgreSqlSchemaFixture.EnvironmentVariable));

    [PostgreSqlSchemaTheory]
    [InlineData("student-1")]
    [InlineData("00000000000000000")]
    [InlineData("xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx")]
    public async Task AllEightOperationsWorkTogetherWithTheRealWorkerEnabled(string studentId)
    {
        using var factory = Factory(worker: true);
        using var client = factory.CreateClient();
        var morning = await CreateClassAsync(client, "Morning");
        var day = await CreateClassAsync(client, "Day");
        var classes = await client.GetFromJsonAsync<List<ClassSummaryResponse>>("/classes");
        Assert.Equal(2, classes!.Count);
        var details = await client.GetFromJsonAsync<ClassSummaryResponse>($"/classes/{morning.Id}");
        Assert.Equal((0, 0, 1), (details!.HeldSeats, details.ConfirmedSeats, details.AvailableSeats));

        var first = await ApplyAsync(client, morning.Id, $" {studentId} ");
        var second = await ApplyAsync(client, day.Id, studentId);
        Assert.Equal(studentId, first.StudentId);
        var publicDetails = await client.GetFromJsonAsync<HoldDetailsResponse>($"/holds/{first.HoldId}");
        Assert.Equal("HELD", publicDetails!.Status);
        Assert.Equal(first.ExpiresAt, publicDetails.ExpiresAt);
        using var duplicate = await SendAsync(client, HttpMethod.Post, $"/classes/{morning.Id}/holds", studentId);
        await AssertProblemAsync(duplicate, HttpStatusCode.Conflict);
        using var full = await SendAsync(client, HttpMethod.Post, $"/classes/{morning.Id}/holds", "another-student");
        await AssertProblemAsync(full, HttpStatusCode.Conflict);
        using var foreignConfirm = await SendAsync(client, HttpMethod.Post, $"/holds/{first.HoldId}/confirm", "another-student");
        await AssertProblemAsync(foreignConfirm, HttpStatusCode.Forbidden);
        using var foreignCancel = await SendAsync(client, HttpMethod.Delete, $"/holds/{second.HoldId}", "another-student");
        await AssertProblemAsync(foreignCancel, HttpStatusCode.Forbidden);

        using var confirmed = await SendAsync(client, HttpMethod.Post, $"/holds/{first.HoldId}/confirm", studentId);
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        Assert.Equal(new ConfirmHoldResponse(first.HoldId, "CONFIRMED"), await confirmed.Content.ReadFromJsonAsync<ConfirmHoldResponse>());
        using var confirmationRetry = await SendAsync(client, HttpMethod.Post, $"/holds/{first.HoldId}/confirm", studentId);
        Assert.Equal(HttpStatusCode.OK, confirmationRetry.StatusCode);
        using var cancelled = await SendAsync(client, HttpMethod.Delete, $"/holds/{second.HoldId}", studentId);
        Assert.Equal(HttpStatusCode.NoContent, cancelled.StatusCode);
        Assert.Equal("", await cancelled.Content.ReadAsStringAsync());
        using var cancellationRetry = await SendAsync(client, HttpMethod.Delete, $"/holds/{second.HoldId}", studentId);
        Assert.Equal(HttpStatusCode.NoContent, cancellationRetry.StatusCode);
        var replacement = await ApplyAsync(client, day.Id, studentId);
        Assert.NotEqual(second.HoldId, replacement.HoldId);

        using var list = await SendAsync(client, HttpMethod.Get, "/me/holds", studentId);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var history = (await list.Content.ReadFromJsonAsync<List<StudentHoldResponse>>())!;
        Assert.Equal(3, history.Count);
        Assert.Equal("CONFIRMED", history.Single(row => row.HoldId == first.HoldId).Status);
        Assert.Equal("CANCELLED", history.Single(row => row.HoldId == second.HoldId).Status);
        Assert.Equal("HELD", history.Single(row => row.HoldId == replacement.HoldId).Status);
        using var missingHeader = await SendAsync(client, HttpMethod.Get, "/me/holds");
        await AssertProblemAsync(missingHeader, HttpStatusCode.BadRequest);
        using var missing = await client.GetAsync($"/holds/{Guid.NewGuid()}");
        await AssertProblemAsync(missing, HttpStatusCode.NotFound);

        await using var context = database.CreateContext();
        var stored = await context.AdmissionHolds.AsNoTracking().ToListAsync();
        Assert.Equal(3, stored.Count);
        Assert.All(stored, hold => Assert.Equal(studentId, hold.StudentId));
        Assert.Single(stored, hold => hold.Status == AdmissionHoldStatus.CONFIRMED && hold.ConfirmedAt.HasValue);
        Assert.Single(stored, hold => hold.Status == AdmissionHoldStatus.CANCELLED && hold.CancelledAt.HasValue);
        var summaries = (await client.GetFromJsonAsync<List<ClassSummaryResponse>>("/classes"))!;
        Assert.All(summaries, summary => Assert.Equal(1, summary.HeldSeats + summary.ConfirmedSeats));
        Assert.All(summaries, summary => Assert.Equal(0, summary.AvailableSeats));
    }

    [PostgreSqlSchemaFact]
    public async Task RealDurationExpiresWithoutRequestsThenAllowsTheSameStudentToReapply()
    {
        using var factory = Factory(worker: true, duration: "1");
        using var client = factory.CreateClient();
        var admissionClass = await CreateClassAsync(client, "Short hold");
        var created = await ApplyAsync(client, admissionClass.Id, "student-1");
        // Observe persistence directly: no HTTP request helps release the hold.
        await using var context = database.CreateContext();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (await context.AdmissionHolds.Where(hold => hold.Id == created.HoldId).Select(hold => hold.Status).SingleAsync(timeout.Token)
            != AdmissionHoldStatus.EXPIRED) await Task.Delay(30, timeout.Token);

        var details = await client.GetFromJsonAsync<HoldDetailsResponse>($"/holds/{created.HoldId}");
        Assert.Equal("EXPIRED", details!.Status);
        Assert.Equal(created.ExpiresAt, details.ExpiresAt);
        var summary = await client.GetFromJsonAsync<ClassSummaryResponse>($"/classes/{admissionClass.Id}");
        Assert.Equal((0, 0, 1), (summary!.HeldSeats, summary.ConfirmedSeats, summary.AvailableSeats));
        using var confirm = await SendAsync(client, HttpMethod.Post, $"/holds/{created.HoldId}/confirm", "student-1");
        await AssertProblemAsync(confirm, HttpStatusCode.Conflict);
        using var cancel = await SendAsync(client, HttpMethod.Delete, $"/holds/{created.HoldId}", "student-1");
        await AssertProblemAsync(cancel, HttpStatusCode.Conflict);
        var replacement = await ApplyAsync(client, admissionClass.Id, "student-1");
        Assert.NotEqual(created.HoldId, replacement.HoldId);
        var original = await context.AdmissionHolds.AsNoTracking().SingleAsync(hold => hold.Id == created.HoldId);
        Assert.Equal(AdmissionHoldStatus.EXPIRED, original.Status);
        Assert.Null(original.ConfirmedAt);
        Assert.Null(original.CancelledAt);
        Assert.Equal(2, await context.AdmissionHolds.CountAsync());
    }

    [PostgreSqlSchemaFact]
    public async Task FailedInsertRollsBackExpiryCleanupAndRetryCommitsBothChanges()
    {
        using var factory = Factory();
        using var client = factory.CreateClient();
        var admissionClass = await CreateClassAsync(client, "Atomic allocation");
        var previous = await ApplyAsync(client, admissionClass.Id, "student-1");
        await using var context = database.CreateContext();
        var expiry = (await context.Database.SqlQuery<DateTime>($"SELECT clock_timestamp() AS \"Value\"").SingleAsync()).AddMinutes(-1);
        await context.AdmissionHolds.ExecuteUpdateAsync(setters => setters.SetProperty(hold => hold.ExpiresAt, expiry));
        await InstallFailureTriggerAsync(insert: true);

        using var failed = await SendAsync(client, HttpMethod.Post, $"/classes/{admissionClass.Id}/holds", "student-1");
        await AssertProblemAsync(failed, HttpStatusCode.InternalServerError);
        var unchanged = await context.AdmissionHolds.AsNoTracking().SingleAsync();
        Assert.Equal(previous.HoldId, unchanged.Id);
        Assert.Equal(AdmissionHoldStatus.HELD, unchanged.Status);
        Assert.Equal(expiry, unchanged.ExpiresAt);

        await DropFailureTriggerAsync();
        var replacement = await ApplyAsync(client, admissionClass.Id, "student-1");
        Assert.NotEqual(previous.HoldId, replacement.HoldId);
        Assert.Equal(2, await context.AdmissionHolds.CountAsync());
        Assert.Equal(AdmissionHoldStatus.EXPIRED,
            (await context.AdmissionHolds.AsNoTracking().SingleAsync(hold => hold.Id == previous.HoldId)).Status);
    }

    [PostgreSqlSchemaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FailedLifecycleWriteLeavesHoldUntouchedAndCanBeRetried(bool confirm)
    {
        using var factory = Factory();
        using var client = factory.CreateClient();
        var admissionClass = await CreateClassAsync(client, "Atomic lifecycle");
        var created = await ApplyAsync(client, admissionClass.Id, "student-1");
        await InstallFailureTriggerAsync(insert: false);
        var method = confirm ? HttpMethod.Post : HttpMethod.Delete;
        var path = $"/holds/{created.HoldId}" + (confirm ? "/confirm" : "");
        using var failed = await SendAsync(client, method, path, "student-1");
        await AssertProblemAsync(failed, HttpStatusCode.InternalServerError);
        await using var context = database.CreateContext();
        var untouched = await context.AdmissionHolds.AsNoTracking().SingleAsync();
        Assert.Equal(AdmissionHoldStatus.HELD, untouched.Status);
        Assert.Equal(created.ExpiresAt, untouched.ExpiresAt);
        Assert.Null(untouched.ConfirmedAt);
        Assert.Null(untouched.CancelledAt);

        await DropFailureTriggerAsync();
        using var retry = await SendAsync(client, method, path, "student-1");
        Assert.Equal(confirm ? HttpStatusCode.OK : HttpStatusCode.NoContent, retry.StatusCode);
        var stored = await context.AdmissionHolds.AsNoTracking().SingleAsync();
        Assert.Equal(confirm ? AdmissionHoldStatus.CONFIRMED : AdmissionHoldStatus.CANCELLED, stored.Status);
        Assert.Equal(confirm, stored.ConfirmedAt.HasValue);
        Assert.Equal(!confirm, stored.CancelledAt.HasValue);
        Assert.Equal(untouched.CreatedAt, stored.CreatedAt);
        Assert.Equal(untouched.ExpiresAt, stored.ExpiresAt);
    }

    private FoundationApiFactory Factory(bool worker = false, string duration = "600") => new(overrides: new Dictionary<string, string?>
    {
        ["ConnectionStrings:DefaultConnection"] = database.ConnectionString,
        ["HOLD_DURATION_SECONDS"] = duration,
        ["Expiration:IntervalMilliseconds"] = "100",
        ["Expiration:BatchSize"] = "10"
    }, runExpirationWorker: worker);

    private static async Task<CreateClassResponse> CreateClassAsync(HttpClient client, string name)
    {
        using var response = await client.PostAsJsonAsync("/classes", new { name, totalSeats = 1 });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CreateClassResponse>())!;
    }
    private static async Task<CreateHoldResponse> ApplyAsync(HttpClient client, Guid classId, string studentId)
    {
        using var response = await SendAsync(client, HttpMethod.Post, $"/classes/{classId}/holds", studentId);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CreateHoldResponse>())!;
    }
    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, string? studentId = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (studentId is not null) request.Headers.TryAddWithoutValidation("X-Student-Id", studentId);
        return await client.SendAsync(request);
    }
    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var text = await response.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(text);
        Assert.Equal((int)expected, body.RootElement.GetProperty("status").GetInt32());
        Assert.StartsWith("/", body.RootElement.GetProperty("instance").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("traceId").GetString()));
        Assert.DoesNotContain("synthetic-write-failure", text);
        Assert.DoesNotContain("workflow_write_failure", text);
        Assert.DoesNotContain("Npgsql", text);
    }
    private async Task InstallFailureTriggerAsync(bool insert)
    {
        await using var context = database.CreateContext();
        await context.Database.ExecuteSqlRawAsync("""
            CREATE OR REPLACE FUNCTION workflow_fail_write() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'synthetic-write-failure'; END; $$;
            """);
        // Static DDL is installed only inside this fixture's owned database.
        var sql = insert
            ? "CREATE TRIGGER workflow_write_failure BEFORE INSERT ON \"AdmissionHolds\" FOR EACH ROW EXECUTE FUNCTION workflow_fail_write()"
            : "CREATE TRIGGER workflow_write_failure BEFORE UPDATE OF status ON \"AdmissionHolds\" FOR EACH ROW EXECUTE FUNCTION workflow_fail_write()";
        await context.Database.ExecuteSqlRawAsync(sql);
    }
    private async Task DropFailureTriggerAsync()
    {
        await using var context = database.CreateContext();
        await context.Database.ExecuteSqlRawAsync("DROP TRIGGER IF EXISTS workflow_write_failure ON \"AdmissionHolds\"");
    }
}
