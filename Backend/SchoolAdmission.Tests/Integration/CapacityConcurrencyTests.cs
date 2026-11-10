using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SchoolAdmission.Api.DTOs.Responses;
using SchoolAdmission.Api.Enums;
using Xunit.Abstractions;

namespace SchoolAdmission.Tests.Integration;

[Trait("Category", "PostgreSQLCapacityConcurrency")]
public sealed class CapacityConcurrencyTests(PostgreSqlSchemaFixture database, ITestOutputHelper output)
    : IClassFixture<PostgreSqlSchemaFixture>, IAsyncLifetime
{
    private readonly string applicationName = $"capacity-test-{Guid.NewGuid():N}";
    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PostgreSqlSchemaFixture.EnvironmentVariable))) return;
        await using var context = database.CreateContext();
        await context.AdmissionHolds.ExecuteDeleteAsync();
        await context.AdmissionClasses.ExecuteDeleteAsync();
    }
    public Task DisposeAsync() => Task.CompletedTask;

    [PostgreSqlSchemaTheory]
    [InlineData(0, 0)]
    [InlineData(3, 2)]
    public async Task OneHundredStudentsReserveExactlyTheRemainingCapacity(int initiallyHeld, int initiallyConfirmed)
    {
        const int capacity = 10;
        const int requestCount = 100;
        var expectedCreated = capacity - initiallyHeld - initiallyConfirmed;
        // All HTTP requests start together. Bound database connections so this test
        // can run alongside other fixtures without exhausting the PostgreSQL server.
        using var factory = new FoundationApiFactory(overrides: new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = new NpgsqlConnectionStringBuilder(database.ConnectionString)
                { ApplicationName = applicationName, Pooling = true, MaxPoolSize = 20 }.ConnectionString
        });
        using var client = factory.CreateClient();
        using var createdClass = await client.PostAsJsonAsync("/classes", new { name = "Contended class", totalSeats = capacity });
        Assert.Equal(HttpStatusCode.Created, createdClass.StatusCode);
        var admissionClass = (await createdClass.Content.ReadFromJsonAsync<CreateClassResponse>())!;
        for (var index = 0; index < initiallyHeld + initiallyConfirmed; index++)
        {
            var studentId = $"existing-student-{index}";
            using var response = await ApplyAsync(client, admissionClass.Id, studentId, CancellationToken.None);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            if (index < initiallyConfirmed)
            {
                var hold = (await response.Content.ReadFromJsonAsync<CreateHoldResponse>())!;
                using var request = new HttpRequestMessage(HttpMethod.Post, $"/holds/{hold.HoldId}/confirm");
                request.Headers.Add("X-Student-Id", studentId);
                using var confirmed = await client.SendAsync(request);
                Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
            }
        }

        await using var blocker = database.CreateContext();
        await using var transaction = await blocker.Database.BeginTransactionAsync();
        await blocker.AdmissionClasses.FromSql($"SELECT * FROM \"AdmissionClasses\" WHERE id = {admissionClass.Id} FOR UPDATE").ToListAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = Enumerable.Range(0, requestCount).Select(async index =>
        {
            var studentId = $"load-student-{index}";
            await start.Task.WaitAsync(timeout.Token);
            return (StudentId: studentId, Response: await ApplyAsync(client, admissionClass.Id, studentId, timeout.Token));
        }).ToArray();
        start.SetResult();
        var blocked = await WaitForContentionAsync(timeout.Token);
        await transaction.CommitAsync(timeout.Token);
        var attempts = await Task.WhenAll(pending).WaitAsync(timeout.Token);
        try
        {
            var created = attempts.Where(attempt => attempt.Response.StatusCode == HttpStatusCode.Created).ToArray();
            var conflicts = attempts.Where(attempt => attempt.Response.StatusCode == HttpStatusCode.Conflict).ToArray();
            Assert.Equal(requestCount, attempts.Length);
            Assert.Equal(expectedCreated, created.Length);
            Assert.Equal(requestCount - expectedCreated, conflicts.Length);
            Assert.All(attempts, attempt => Assert.Contains(attempt.Response.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.Conflict }));
            var successfulHolds = new List<CreateHoldResponse>();
            foreach (var attempt in created)
            {
                var hold = (await attempt.Response.Content.ReadFromJsonAsync<CreateHoldResponse>())!;
                Assert.Equal(admissionClass.Id, hold.ClassId);
                Assert.Equal(attempt.StudentId, hold.StudentId);
                Assert.Equal("HELD", hold.Status);
                successfulHolds.Add(hold);
            }
            Assert.Equal(expectedCreated, successfulHolds.Select(hold => hold.HoldId).Distinct().Count());
            foreach (var attempt in conflicts)
            {
                Assert.Equal("application/problem+json", attempt.Response.Content.Headers.ContentType?.MediaType);
                using var body = JsonDocument.Parse(await attempt.Response.Content.ReadAsStringAsync());
                Assert.Equal(409, body.RootElement.GetProperty("status").GetInt32());
                Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("traceId").GetString()));
            }

            await using var context = database.CreateContext();
            var now = await context.Database.SqlQuery<DateTime>($"SELECT clock_timestamp() AS \"Value\"").SingleAsync();
            var persisted = await context.AdmissionHolds.AsNoTracking().Where(hold => hold.ClassId == admissionClass.Id).ToListAsync();
            var held = persisted.Count(hold => hold.Status == AdmissionHoldStatus.HELD && hold.ExpiresAt > now);
            var confirmed = persisted.Count(hold => hold.Status == AdmissionHoldStatus.CONFIRMED);
            var distinctStudents = persisted.Select(hold => hold.StudentId).Distinct().Count();
            Assert.Equal(capacity, persisted.Count);
            Assert.Equal(capacity, distinctStudents);
            Assert.Equal(initiallyHeld + expectedCreated, held);
            Assert.Equal(initiallyConfirmed, confirmed);
            Assert.Equal(capacity, held + confirmed);
            Assert.True(held + confirmed <= admissionClass.TotalSeats);
            foreach (var hold in successfulHolds)
            {
                var stored = Assert.Single(persisted, row => row.Id == hold.HoldId);
                Assert.Equal(hold.StudentId, stored.StudentId);
                Assert.Equal(hold.ExpiresAt, stored.ExpiresAt);
            }
            var summary = (await client.GetFromJsonAsync<ClassSummaryResponse>($"/classes/{admissionClass.Id}"))!;
            Assert.Equal((capacity, held, confirmed, 0), (summary.TotalSeats, summary.HeldSeats, summary.ConfirmedSeats, summary.AvailableSeats));

            // Stable metrics only: never print identifiers, database names, or connection values.
            output.WriteLine($"Requests={requestCount}; Capacity={capacity}; InitiallyHeld={initiallyHeld}; InitiallyConfirmed={initiallyConfirmed}");
            output.WriteLine($"HTTP 201={created.Length}; HTTP 409={conflicts.Length}; Other={attempts.Length - created.Length - conflicts.Length}");
            output.WriteLine($"Database held={held}; confirmed={confirmed}; occupied={held + confirmed}; totalRows={persisted.Count}; distinctStudents={distinctStudents}");
            output.WriteLine($"Invariant: occupied <= totalSeats => {held + confirmed} <= {capacity} (PASS)");
            output.WriteLine($"Blocked allocation requests observed before lock release: {blocked}");
        }
        finally { foreach (var attempt in attempts) attempt.Response.Dispose(); }
    }

    private async Task<int> WaitForContentionAsync(CancellationToken cancellationToken)
    {
        await using var context = database.CreateContext();
        while (true)
        {
            var count = await context.Database.SqlQuery<int>($"""
                SELECT count(*)::int AS "Value" FROM pg_stat_activity WHERE datname = current_database()
                    AND application_name = {applicationName} AND cardinality(pg_blocking_pids(pid)) > 0
                """).SingleAsync(cancellationToken);
            if (count >= 10) return count;
            await Task.Delay(20, cancellationToken);
        }
    }

    private static async Task<HttpResponseMessage> ApplyAsync(HttpClient client, Guid classId, string studentId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/classes/{classId}/holds");
        request.Headers.Add("X-Student-Id", studentId);
        return await client.SendAsync(request, cancellationToken);
    }
}
