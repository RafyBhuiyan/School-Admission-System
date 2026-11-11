using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SchoolAdmission.Api.DTOs.Responses;
using SchoolAdmission.Api.Enums;
using Xunit.Abstractions;

namespace SchoolAdmission.Tests.Integration;

[Trait("Category", "PostgreSQLMultiInstance")]
public sealed class MultiInstanceConcurrencyTests(PostgreSqlSchemaFixture database, ITestOutputHelper output)
    : IClassFixture<PostgreSqlSchemaFixture>
{
    [PostgreSqlSchemaTheory]
    [InlineData(0, 0)]
    [InlineData(3, 2)]
    public async Task SeparateProcessesShareCapacity(int initiallyHeld, int initiallyConfirmed)
    {
        var prefix = $"multi-instance-{Guid.NewGuid():N}";
        string Connection(string suffix) => new NpgsqlConnectionStringBuilder(database.ConnectionString)
            { ApplicationName = prefix + suffix, Pooling = true, MaxPoolSize = 10 }.ConnectionString;
        await using var first = await BackendProcess.StartAsync(Connection("-a"));
        await using var second = await BackendProcess.StartAsync(Connection("-b"));
        Assert.NotEqual(first.ProcessId, second.ProcessId);
        Assert.NotEqual(first.Client.BaseAddress!.Port, second.Client.BaseAddress!.Port);
        using var createdClass = await first.Client.PostAsJsonAsync("/classes", new { name = prefix, totalSeats = 10 });
        Assert.Equal(HttpStatusCode.Created, createdClass.StatusCode);
        var admissionClass = (await createdClass.Content.ReadFromJsonAsync<CreateClassResponse>())!;
        for (var index = 0; index < initiallyHeld + initiallyConfirmed; index++)
        {
            var student = $"existing-{index}";
            using var response = await Apply(first.Client, admissionClass.Id, student, CancellationToken.None);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            if (index < initiallyConfirmed)
            {
                var hold = (await response.Content.ReadFromJsonAsync<CreateHoldResponse>())!;
                using var request = new HttpRequestMessage(HttpMethod.Post, $"/holds/{hold.HoldId}/confirm");
                request.Headers.Add("X-Student-Id", student);
                using var confirmed = await second.Client.SendAsync(request);
                Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
            }
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        await using var blocker = database.CreateContext();
        await using var transaction = await blocker.Database.BeginTransactionAsync(timeout.Token);
        await blocker.AdmissionClasses.FromSql($"SELECT * FROM \"AdmissionClasses\" WHERE id = {admissionClass.Id} FOR UPDATE")
            .ToListAsync(timeout.Token);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = Enumerable.Range(0, 100).Select(async index =>
        {
            await start.Task.WaitAsync(timeout.Token);
            var instance = index % 2;
            var student = $"synthetic-burst-{index}";
            return (Instance: instance, Student: student, Response: await Apply(instance == 0 ? first.Client : second.Client,
                admissionClass.Id, student, timeout.Token));
        }).ToArray();
        start.SetResult();
        // Observe actual blocked allocation sessions from both OS processes before releasing capacity.
        await using var observer = database.CreateContext();
        var firstName = prefix + "-a";
        var secondName = prefix + "-b";
        while (await observer.Database.SqlQuery<int>($"""
            SELECT count(DISTINCT application_name)::int AS "Value" FROM pg_stat_activity
            WHERE datname = current_database() AND application_name IN ({firstName}, {secondName})
                AND cardinality(pg_blocking_pids(pid)) > 0
            """).SingleAsync(timeout.Token) < 2) await Task.Delay(20, timeout.Token);
        await transaction.CommitAsync(timeout.Token);
        var results = await Task.WhenAll(pending).WaitAsync(timeout.Token);
        try
        {
            var expected = 10 - initiallyHeld - initiallyConfirmed;
            var successes = results.Where(row => row.Response.StatusCode == HttpStatusCode.Created).ToArray();
            Assert.Equal(expected, successes.Length);
            Assert.Equal(100 - expected, results.Count(row => row.Response.StatusCode == HttpStatusCode.Conflict));
            Assert.All(results, row => Assert.Contains(row.Response.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.Conflict }));
            var rows = await observer.AdmissionHolds.AsNoTracking().Where(row => row.ClassId == admissionClass.Id).ToListAsync(timeout.Token);
            var now = await observer.Database.SqlQuery<DateTime>($"SELECT clock_timestamp() AS \"Value\"").SingleAsync(timeout.Token);
            Assert.Equal(10, rows.Count);
            Assert.Equal(10, rows.Select(row => row.StudentId).Distinct().Count());
            Assert.Equal(initiallyConfirmed, rows.Count(row => row.Status == AdmissionHoldStatus.CONFIRMED));
            Assert.Equal(10 - initiallyConfirmed, rows.Count(row => row.Status == AdmissionHoldStatus.HELD && row.ExpiresAt > now));
            foreach (var success in successes)
            {
                var hold = (await success.Response.Content.ReadFromJsonAsync<CreateHoldResponse>(timeout.Token))!;
                var stored = Assert.Single(rows, row => row.Id == hold.HoldId);
                Assert.Equal(success.Student, stored.StudentId);
                Assert.Equal(hold.ExpiresAt, stored.ExpiresAt);
            }
            var summaryA = (await first.Client.GetFromJsonAsync<ClassSummaryResponse>($"/classes/{admissionClass.Id}", timeout.Token))!;
            var summaryB = (await second.Client.GetFromJsonAsync<ClassSummaryResponse>($"/classes/{admissionClass.Id}", timeout.Token))!;
            Assert.Equal(summaryA, summaryB);
            Assert.Equal((10, 10 - initiallyConfirmed, initiallyConfirmed, 0),
                (summaryA.TotalSeats, summaryA.HeldSeats, summaryA.ConfirmedSeats, summaryA.AvailableSeats));
            output.WriteLine($"Distinct OS processes: 2; distinct listening ports: 2; shared database: 1; contending instances observed: 2");
            output.WriteLine($"Requests=100 (50 per instance); Capacity=10; InitiallyHeld={initiallyHeld}; InitiallyConfirmed={initiallyConfirmed}");
            output.WriteLine($"HTTP 201={successes.Length}; HTTP 409={100 - successes.Length}; Other=0; persisted occupancy=10; distinct students=10; available=0");
            foreach (var instance in new[] { 0, 1 }) output.WriteLine($"Instance {instance + 1}: HTTP 201={results.Count(row => row.Instance == instance && row.Response.StatusCode == HttpStatusCode.Created)}; HTTP 409={results.Count(row => row.Instance == instance && row.Response.StatusCode == HttpStatusCode.Conflict)}");
            output.WriteLine("Both API summaries agree; response IDs/expiries match database rows; capacity invariant PASS.");
        }
        finally { foreach (var result in results) result.Response.Dispose(); }
    }

    private static async Task<HttpResponseMessage> Apply(HttpClient client, Guid classId, string studentId, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/classes/{classId}/holds");
        request.Headers.Add("X-Student-Id", studentId);
        return await client.SendAsync(request, token);
    }
}
