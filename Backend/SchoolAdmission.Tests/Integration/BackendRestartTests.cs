using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using SchoolAdmission.Api.DTOs.Responses;
using SchoolAdmission.Api.Enums;
using SchoolAdmission.Api.Models.Entities;

namespace SchoolAdmission.Tests.Integration;

[Trait("Category", "PostgreSQLRestart")]
public sealed class BackendRestartTests(PostgreSqlSchemaFixture database)
    : IClassFixture<PostgreSqlSchemaFixture>, IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PostgreSqlSchemaFixture.EnvironmentVariable))) return;
        await using var context = database.CreateContext();
        await context.AdmissionHolds.ExecuteDeleteAsync();
        await context.AdmissionClasses.ExecuteDeleteAsync();
    }
    public Task DisposeAsync() => Task.CompletedTask;

    [PostgreSqlSchemaFact]
    public async Task ProcessRestartPreservesHistoryOwnershipAndRetryResultsWhileWorkerLeavesTerminalStatesUntouched()
    {
        CreateClassResponse admissionClass;
        CreateHoldResponse active, confirmed, cancelled, expired;
        HoldSnapshot[] before;
        (DateTime Created, DateTime Updated) classTimes;
        await using (var first = await BackendProcess.StartAsync(database.ConnectionString))
        {
            admissionClass = await CreateClassAsync(first.Client, 5);
            active = await ApplyAsync(first.Client, admissionClass.Id, "active-student");
            confirmed = await ApplyAsync(first.Client, admissionClass.Id, "confirmed-student");
            cancelled = await ApplyAsync(first.Client, admissionClass.Id, "cancelled-student");
            expired = await ApplyAsync(first.Client, admissionClass.Id, "expired-student");
            using var confirm = await MutateAsync(first.Client, confirmed, confirm: true);
            Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
            using var cancel = await MutateAsync(first.Client, cancelled, confirm: false);
            Assert.Equal(HttpStatusCode.NoContent, cancel.StatusCode);
            await using var context = database.CreateContext();
            var past = (await TimeAsync()).AddMinutes(-1);
            // Advance the historical deadlines without waiting ten minutes. The active
            // hold stays valid; the real worker must only change the overdue HELD row.
            await context.AdmissionHolds.Where(hold => hold.Id != active.HoldId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(hold => hold.ExpiresAt, past));
            await WaitForStatusAsync(expired.HoldId, AdmissionHoldStatus.EXPIRED);
            before = await SnapshotAsync();
            var storedClass = await context.AdmissionClasses.AsNoTracking().SingleAsync();
            classTimes = (storedClass.CreatedAt, storedClass.UpdatedAt);
        } // Terminate the OS process, not merely its HTTP client.

        Guid offlineHoldId;
        await using (var context = database.CreateContext())
        {
            var overdue = new AdmissionHold { ClassId = admissionClass.Id, StudentId = "offline-student",
                ExpiresAt = (await TimeAsync()).AddMinutes(-1) };
            context.AdmissionHolds.Add(overdue);
            await context.SaveChangesAsync();
            offlineHoldId = overdue.Id;
        }
        await using var restarted = await BackendProcess.StartAsync(database.ConnectionString);
        // This newly expired row proves a worker cycle ran in the restarted process.
        await WaitForStatusAsync(offlineHoldId, AdmissionHoldStatus.EXPIRED);
        var after = (await SnapshotAsync()).Where(row => row.Id != offlineHoldId).ToArray();
        Assert.Equal(before, after);
        await using (var context = database.CreateContext())
        {
            var storedClass = await context.AdmissionClasses.AsNoTracking().SingleAsync();
            Assert.Equal((classTimes.Created, classTimes.Updated), (storedClass.CreatedAt, storedClass.UpdatedAt));
            Assert.Equal(admissionClass.Name, storedClass.Name);
        }
        var summary = await restarted.Client.GetFromJsonAsync<ClassSummaryResponse>($"/classes/{admissionClass.Id}");
        Assert.Equal((5, 1, 1, 3), (summary!.TotalSeats, summary.HeldSeats, summary.ConfirmedSeats, summary.AvailableSeats));
        var classes = await restarted.Client.GetFromJsonAsync<List<ClassSummaryResponse>>("/classes");
        Assert.Equal(admissionClass.Id, Assert.Single(classes!).Id);
        foreach (var (hold, expected) in new[] { (active, "HELD"), (confirmed, "CONFIRMED"), (cancelled, "CANCELLED"), (expired, "EXPIRED") })
        {
            var details = await restarted.Client.GetFromJsonAsync<HoldDetailsResponse>($"/holds/{hold.HoldId}");
            Assert.Equal(expected, details!.Status);
            using var list = await SendAsync(restarted.Client, HttpMethod.Get, "/me/holds", hold.StudentId);
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
            Assert.Equal(expected, Assert.Single((await list.Content.ReadFromJsonAsync<List<StudentHoldResponse>>())!).Status);
            using var wrongConfirm = await SendAsync(restarted.Client, HttpMethod.Post, $"/holds/{hold.HoldId}/confirm", "wrong-student");
            Assert.Equal(HttpStatusCode.Forbidden, wrongConfirm.StatusCode);
            using var wrongCancel = await SendAsync(restarted.Client, HttpMethod.Delete, $"/holds/{hold.HoldId}", "wrong-student");
            Assert.Equal(HttpStatusCode.Forbidden, wrongCancel.StatusCode);
        }
        for (var retry = 0; retry < 2; retry++)
        {
            using var confirmation = await MutateAsync(restarted.Client, confirmed, confirm: true);
            Assert.Equal(HttpStatusCode.OK, confirmation.StatusCode);
            Assert.Equal(new ConfirmHoldResponse(confirmed.HoldId, "CONFIRMED"), await confirmation.Content.ReadFromJsonAsync<ConfirmHoldResponse>());
            using var cancellation = await MutateAsync(restarted.Client, cancelled, confirm: false);
            Assert.Equal(HttpStatusCode.NoContent, cancellation.StatusCode);
        }
        Assert.Equal(before, (await SnapshotAsync()).Where(row => row.Id != offlineHoldId).ToArray());
    }

    [PostgreSqlSchemaFact]
    public async Task HoldExpiringDuringProcessDowntimeIsReleasedOnStartupWithoutAStudentRequest()
    {
        CreateClassResponse admissionClass;
        CreateHoldResponse created;
        HoldSnapshot before;
        await using (var first = await BackendProcess.StartAsync(database.ConnectionString, durationSeconds: 5))
        {
            admissionClass = await CreateClassAsync(first.Client, 1);
            created = await ApplyAsync(first.Client, admissionClass.Id, "student-1");
            before = Assert.Single(await SnapshotAsync());
        }
        Assert.Equal(AdmissionHoldStatus.HELD, Assert.Single(await SnapshotAsync()).Status);
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
        {
            while (await TimeAsync() < created.ExpiresAt) await Task.Delay(30, timeout.Token);
        }
        // With the process stopped, expiry has passed but nobody persists it yet.
        Assert.Equal(before, Assert.Single(await SnapshotAsync()));
        await using var restarted = await BackendProcess.StartAsync(database.ConnectionString);
        await WaitForStatusAsync(created.HoldId, AdmissionHoldStatus.EXPIRED); // Database polling only.
        var stored = Assert.Single(await SnapshotAsync());
        Assert.Equal(before with { Status = AdmissionHoldStatus.EXPIRED }, stored);
        var replacement = await ApplyAsync(restarted.Client, admissionClass.Id, "student-2");
        Assert.NotEqual(created.HoldId, replacement.HoldId);
        using var oldConfirmation = await MutateAsync(restarted.Client, created, confirm: true);
        Assert.Equal(HttpStatusCode.Conflict, oldConfirmation.StatusCode);
        using var oldCancellation = await MutateAsync(restarted.Client, created, confirm: false);
        Assert.Equal(HttpStatusCode.Conflict, oldCancellation.StatusCode);
        var summary = await restarted.Client.GetFromJsonAsync<ClassSummaryResponse>($"/classes/{admissionClass.Id}");
        Assert.Equal((1, 0, 0), (summary!.HeldSeats, summary.ConfirmedSeats, summary.AvailableSeats));
        Assert.Equal(2, (await SnapshotAsync()).Length);
    }

    private async Task<HoldSnapshot[]> SnapshotAsync()
    {
        await using var context = database.CreateContext();
        return await context.AdmissionHolds.AsNoTracking().OrderBy(hold => hold.Id).Select(hold => new HoldSnapshot(
            hold.Id, hold.ClassId, hold.StudentId, hold.Status, hold.CreatedAt, hold.ExpiresAt, hold.ConfirmedAt, hold.CancelledAt)).ToArrayAsync();
    }
    private async Task<DateTime> TimeAsync()
    {
        await using var context = database.CreateContext();
        return await context.Database.SqlQuery<DateTime>($"SELECT clock_timestamp() AS \"Value\"").SingleAsync();
    }
    private async Task WaitForStatusAsync(Guid id, AdmissionHoldStatus expected)
    {
        await using var context = database.CreateContext();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (await context.AdmissionHolds.Where(hold => hold.Id == id).Select(hold => hold.Status).SingleAsync(timeout.Token) != expected)
            await Task.Delay(30, timeout.Token);
    }
    private static async Task<CreateClassResponse> CreateClassAsync(HttpClient client, int capacity)
    {
        using var response = await client.PostAsJsonAsync("/classes", new { name = "Persistent class", totalSeats = capacity });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CreateClassResponse>())!;
    }
    private static async Task<CreateHoldResponse> ApplyAsync(HttpClient client, Guid classId, string studentId)
    {
        using var response = await SendAsync(client, HttpMethod.Post, $"/classes/{classId}/holds", studentId);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CreateHoldResponse>())!;
    }
    private static Task<HttpResponseMessage> MutateAsync(HttpClient client, CreateHoldResponse hold, bool confirm) =>
        SendAsync(client, confirm ? HttpMethod.Post : HttpMethod.Delete,
            $"/holds/{hold.HoldId}" + (confirm ? "/confirm" : ""), hold.StudentId);
    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, string studentId)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Student-Id", studentId);
        return await client.SendAsync(request);
    }
    private sealed record HoldSnapshot(Guid Id, Guid ClassId, string StudentId, AdmissionHoldStatus Status,
        DateTime CreatedAt, DateTime ExpiresAt, DateTime? ConfirmedAt, DateTime? CancelledAt);
}
