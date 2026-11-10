using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SchoolAdmission.Api.Configuration;
using SchoolAdmission.Api.Data;
using SchoolAdmission.Api.DTOs.Responses;
using SchoolAdmission.Api.Enums;
using SchoolAdmission.Api.Models.Entities;
using SchoolAdmission.Api.Services;

namespace SchoolAdmission.Tests.Integration;

[Trait("Category", "PostgreSQLStudentApplications")]
public sealed class StudentApplicationTests(PostgreSqlSchemaFixture database)
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

    [PostgreSqlSchemaTheory]
    [InlineData(AdmissionHoldStatus.HELD, -60, "EXPIRED")]
    [InlineData(AdmissionHoldStatus.HELD, 60, "HELD")]
    [InlineData(AdmissionHoldStatus.CONFIRMED, -60, "CONFIRMED")]
    [InlineData(AdmissionHoldStatus.CONFIRMED, 60, "CONFIRMED")]
    [InlineData(AdmissionHoldStatus.EXPIRED, -60, "EXPIRED")]
    [InlineData(AdmissionHoldStatus.EXPIRED, 60, "EXPIRED")]
    [InlineData(AdmissionHoldStatus.CANCELLED, -60, "CANCELLED")]
    [InlineData(AdmissionHoldStatus.CANCELLED, 60, "CANCELLED")]
    public async Task ListReturnsExactFieldsAndEffectiveStatusWithoutMutatingHistory(AdmissionHoldStatus status, int minutes, string expected)
    {
        AdmissionHold hold;
        await using (var seed = database.CreateContext())
        {
            hold = NewHold("Student-1", status, (await TimeAsync(seed)).AddMinutes(minutes), "Morning Shift");
            seed.AdmissionHolds.Add(hold);
            await seed.SaveChangesAsync();
        }
        using var factory = Factory();
        using var client = factory.CreateClient();
        using var response = await ListAsync(client, " Student-1 ");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var row = Assert.Single(json.RootElement.EnumerateArray());
        Assert.Equal(new[] { "classId", "className", "expiresAt", "holdId", "status" }, row.EnumerateObject().Select(property => property.Name).Order());
        var application = Assert.Single((await response.Content.ReadFromJsonAsync<List<StudentHoldResponse>>())!);
        Assert.Equal(new StudentHoldResponse(hold.Id, hold.ClassId, "Morning Shift", expected, hold.ExpiresAt), application);
        Assert.Equal(DateTimeKind.Utc, application.ExpiresAt.Kind);
        await using var context = database.CreateContext();
        var stored = await context.AdmissionHolds.AsNoTracking().SingleAsync();
        Assert.Equal((hold.Status, hold.CreatedAt, hold.ExpiresAt, hold.ConfirmedAt, hold.CancelledAt),
            (stored.Status, stored.CreatedAt, stored.ExpiresAt, stored.ConfirmedAt, stored.CancelledAt));
    }

    [PostgreSqlSchemaFact]
    public async Task ListIncludesAllClassesAndHistoryInNewestOrderWithoutLeakingOtherStudents()
    {
        const string student = "000Student-1";
        AdmissionHold[] expected;
        await using (var seed = database.CreateContext())
        {
            var now = await TimeAsync(seed);
            var active = NewHold(student, AdmissionHoldStatus.HELD, now.AddHours(1), "Morning");
            var expired = NewHold(student, AdmissionHoldStatus.EXPIRED, now.AddHours(-1));
            var cancelled = NewHold(student, AdmissionHoldStatus.CANCELLED, now.AddHours(-1));
            expired.AdmissionClass = cancelled.AdmissionClass = active.AdmissionClass;
            expired.ClassId = cancelled.ClassId = active.ClassId;
            var confirmed = NewHold(student, AdmissionHoldStatus.CONFIRMED, now.AddHours(-1), "Day");
            var overdue = NewHold(student, AdmissionHoldStatus.HELD, now.AddHours(-1), "Evening");
            expected = [active, expired, cancelled, confirmed, overdue];
            for (var index = 0; index < expected.Length; index++) expected[index].CreatedAt = now.AddMinutes(-index);
            seed.AdmissionHolds.AddRange(expected);
            seed.AdmissionHolds.Add(NewHold("000student-1", AdmissionHoldStatus.HELD, now.AddHours(1)));
            seed.AdmissionHolds.Add(NewHold("Student-1", AdmissionHoldStatus.HELD, now.AddHours(1)));
            await seed.SaveChangesAsync();
        }
        using var factory = Factory();
        using var client = factory.CreateClient();
        using var response = await ListAsync(client, student, "/me/holds?studentId=Student-1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rows = (await response.Content.ReadFromJsonAsync<List<StudentHoldResponse>>())!;
        Assert.Equal(expected.Select(hold => hold.Id), rows.Select(row => row.HoldId));
        Assert.Equal(new[] { "Morning", "Morning", "Morning", "Day", "Evening" }, rows.Select(row => row.ClassName));
        Assert.Equal(new[] { "HELD", "EXPIRED", "CANCELLED", "CONFIRMED", "EXPIRED" }, rows.Select(row => row.Status));
    }

    [PostgreSqlSchemaFact]
    public async Task StudentWithNoApplicationsGetsEmptyArrayEvenWhenOthersHaveApplications()
    {
        await using (var seed = database.CreateContext())
        {
            seed.AdmissionHolds.Add(NewHold("other-student", AdmissionHoldStatus.HELD, (await TimeAsync(seed)).AddHours(1)));
            await seed.SaveChangesAsync();
        }
        using var factory = Factory();
        using var client = factory.CreateClient();
        using var response = await ListAsync(client, "student-1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("[]", await response.Content.ReadAsStringAsync());
    }

    [PostgreSqlSchemaTheory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx")]
    public async Task InvalidHeaderReturns400ProblemDetails(string? studentId)
    {
        using var factory = Factory();
        using var client = factory.CreateClient();
        using var response = await ListAsync(client, studentId);
        await AssertBadRequestAsync(response);
    }

    [PostgreSqlSchemaFact]
    public async Task MultipleHeadersAreRejectedAndTrimmed64CharacterIdIsAccepted()
    {
        var studentId = new string('a', 64);
        await using (var seed = database.CreateContext())
        {
            seed.AdmissionHolds.Add(NewHold(studentId, AdmissionHoldStatus.HELD, (await TimeAsync(seed)).AddHours(1)));
            await seed.SaveChangesAsync();
        }
        using var factory = Factory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/me/holds");
        request.Headers.TryAddWithoutValidation("X-Student-Id", new[] { studentId, "other" });
        using var invalid = await client.SendAsync(request);
        await AssertBadRequestAsync(invalid);
        using var valid = await ListAsync(client, $"  {studentId}  ");
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        Assert.Single((await valid.Content.ReadFromJsonAsync<List<StudentHoldResponse>>())!);
    }

    [PostgreSqlSchemaFact]
    public async Task OneCapturedInstantControlsTheExpiryBoundaryAcrossTheList()
    {
        DateTime now;
        AdmissionHold[] holds;
        await using (var seed = database.CreateContext())
        {
            now = await TimeAsync(seed);
            holds = new[] { -1, 0, 1 }.Select(offset => NewHold("student-1", AdmissionHoldStatus.HELD, now.AddTicks(offset * 10L))).ToArray();
            seed.AdmissionHolds.AddRange(holds);
            await seed.SaveChangesAsync();
        }
        var clock = new FixedDatabaseTimeInterceptor(now);
        await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(database.ConnectionString).AddInterceptors(clock).Options);
        var rows = await Service(context).GetForStudentAsync("student-1", CancellationToken.None);
        foreach (var hold in holds) Assert.Equal(hold.ExpiresAt > now ? "HELD" : "EXPIRED", rows.Single(row => row.HoldId == hold.Id).Status);
        Assert.Equal(3, rows.Count);
        Assert.Equal(1, clock.ClockReads);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [PostgreSqlSchemaFact]
    public async Task ExistingTransactionUsesFreshDatabaseTimeAndDoesNotPersistExpiry()
    {
        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var transactionStart = await context.Database.SqlQuery<DateTime>($"SELECT CURRENT_TIMESTAMP AS \"Value\"").SingleAsync();
        var expiresAt = await TimeAsync(context);
        Assert.True(expiresAt > transactionStart);
        var hold = NewHold("student-1", AdmissionHoldStatus.HELD, expiresAt);
        context.AdmissionHolds.Add(hold);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var row = Assert.Single(await Service(context).GetForStudentAsync("student-1", CancellationToken.None));
        Assert.Equal("EXPIRED", row.Status);
        Assert.Equal(expiresAt, row.ExpiresAt);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Equal(AdmissionHoldStatus.HELD, (await context.AdmissionHolds.AsNoTracking().SingleAsync()).Status);
        await transaction.RollbackAsync();
    }

    [PostgreSqlSchemaFact]
    public async Task ListReflectsApplicationsAfterCreationConfirmationAndCancellation()
    {
        using var factory = Factory();
        using var client = factory.CreateClient();
        var ids = new List<Guid>();
        for (var index = 0; index < 2; index++)
        {
            using var createdClass = await client.PostAsJsonAsync("/classes", new { name = $"Class {index}", totalSeats = 2 });
            Assert.Equal(HttpStatusCode.Created, createdClass.StatusCode);
            var admissionClass = (await createdClass.Content.ReadFromJsonAsync<CreateClassResponse>())!;
            using var apply = new HttpRequestMessage(HttpMethod.Post, $"/classes/{admissionClass.Id}/holds");
            apply.Headers.Add("X-Student-Id", "student-1");
            using var response = await client.SendAsync(apply);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            ids.Add((await response.Content.ReadFromJsonAsync<CreateHoldResponse>())!.HoldId);
        }
        using var confirm = new HttpRequestMessage(HttpMethod.Post, $"/holds/{ids[0]}/confirm");
        confirm.Headers.Add("X-Student-Id", "student-1");
        using var confirmed = await client.SendAsync(confirm);
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        using var cancel = new HttpRequestMessage(HttpMethod.Delete, $"/holds/{ids[1]}");
        cancel.Headers.Add("X-Student-Id", "student-1");
        using var cancelled = await client.SendAsync(cancel);
        Assert.Equal(HttpStatusCode.NoContent, cancelled.StatusCode);
        using var list = await ListAsync(client, "student-1");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var rows = (await list.Content.ReadFromJsonAsync<List<StudentHoldResponse>>())!;
        Assert.Equal(2, rows.Count);
        Assert.Equal("CONFIRMED", rows.Single(row => row.HoldId == ids[0]).Status);
        Assert.Equal("CANCELLED", rows.Single(row => row.HoldId == ids[1]).Status);
    }

    [PostgreSqlSchemaFact]
    public async Task OpenApiDocumentsAllEightOperationsAndTheStudentListHeader()
    {
        using var factory = Factory();
        using var client = factory.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        var paths = document.RootElement.GetProperty("paths");
        foreach (var (path, method) in new[] { ("/classes", "post"), ("/classes", "get"), ("/classes/{classId}", "get"),
            ("/classes/{classId}/holds", "post"), ("/holds/{holdId}", "get"), ("/holds/{holdId}/confirm", "post"),
            ("/holds/{holdId}", "delete"), ("/me/holds", "get") }) Assert.True(paths.GetProperty(path).TryGetProperty(method, out _));
        var list = paths.GetProperty("/me/holds").GetProperty("get");
        var header = Assert.Single(list.GetProperty("parameters").EnumerateArray());
        Assert.Equal("X-Student-Id", header.GetProperty("name").GetString());
        Assert.True(header.GetProperty("required").GetBoolean());
        Assert.True(list.GetProperty("responses").TryGetProperty("200", out _));
        Assert.True(list.GetProperty("responses").TryGetProperty("400", out _));
    }

    private FoundationApiFactory Factory() => new(overrides: new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = database.ConnectionString });
    private static HoldService Service(AppDbContext context) => new(context, Options.Create(new HoldOptions()));
    private static AdmissionHold NewHold(string studentId, AdmissionHoldStatus status, DateTime expiry, string? className = null)
    {
        var admissionClass = new AdmissionClass { Name = className ?? $"Class {Guid.NewGuid():N}", TotalSeats = 10 };
        return new AdmissionHold { AdmissionClass = admissionClass, ClassId = admissionClass.Id, StudentId = studentId,
            Status = status, ExpiresAt = expiry, ConfirmedAt = status == AdmissionHoldStatus.CONFIRMED ? expiry.AddHours(-2) : null,
            CancelledAt = status == AdmissionHoldStatus.CANCELLED ? expiry.AddHours(-2) : null };
    }
    private static Task<DateTime> TimeAsync(AppDbContext context) => context.Database.SqlQuery<DateTime>($"SELECT clock_timestamp() AS \"Value\"").SingleAsync();
    private static async Task<HttpResponseMessage> ListAsync(HttpClient client, string? studentId, string path = "/me/holds")
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (studentId is not null) request.Headers.TryAddWithoutValidation("X-Student-Id", studentId);
        return await client.SendAsync(request);
    }
    private static async Task AssertBadRequestAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(400, json.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("/me/holds", json.RootElement.GetProperty("instance").GetString());
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("traceId").GetString()));
    }
}
