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

[Trait("Category", "PostgreSQLHoldRetrieval")]
public sealed class HoldRetrievalTests(PostgreSqlSchemaFixture database)
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
    public async Task GetWithoutStudentHeaderReturnsCurrentStatusAndDatabaseTimeWithoutChangingTheRow(
        AdmissionHoldStatus status, int expiryMinutes, string expectedStatus)
    {
        AdmissionHold hold;
        await using (var seed = database.CreateContext())
        {
            hold = NewHold(status, (await ReadTimeAsync(seed)).AddMinutes(expiryMinutes));
            seed.AdmissionHolds.Add(hold);
            await seed.SaveChangesAsync();
        }

        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await using var context = database.CreateContext();
        var before = await ReadTimeAsync(context);
        using var response = await client.GetAsync($"/holds/{hold.Id}");
        var after = await ReadTimeAsync(context);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(new[] { "classId", "expiresAt", "holdId", "serverTime", "status" },
            body.RootElement.EnumerateObject().Select(property => property.Name).Order());
        var details = (await response.Content.ReadFromJsonAsync<HoldDetailsResponse>())!;
        Assert.Equal(hold.Id, details.HoldId);
        Assert.Equal(hold.ClassId, details.ClassId);
        Assert.Equal(expectedStatus, details.Status);
        Assert.Equal(hold.ExpiresAt, details.ExpiresAt);
        Assert.Equal(DateTimeKind.Utc, details.ExpiresAt.Kind);
        Assert.Equal(DateTimeKind.Utc, details.ServerTime.Kind);
        Assert.InRange(details.ServerTime, before, after);

        var stored = await context.AdmissionHolds.AsNoTracking().SingleAsync();
        Assert.Equal((hold.Id, hold.ClassId, hold.StudentId, hold.Status, hold.CreatedAt,
                hold.ExpiresAt, hold.ConfirmedAt, hold.CancelledAt),
            (stored.Id, stored.ClassId, stored.StudentId, stored.Status, stored.CreatedAt,
                stored.ExpiresAt, stored.ConfirmedAt, stored.CancelledAt));
    }

    [PostgreSqlSchemaTheory]
    [InlineData(-1, "EXPIRED")]
    [InlineData(0, "EXPIRED")]
    [InlineData(1, "HELD")]
    public async Task ExpiryBoundaryAndReturnedServerTimeUseTheSameInstant(int offsetMicroseconds, string expectedStatus)
    {
        DateTime now;
        AdmissionHold hold;
        await using (var seed = database.CreateContext())
        {
            now = await ReadTimeAsync(seed);
            hold = NewHold(AdmissionHoldStatus.HELD, now.AddTicks(offsetMicroseconds * 10L));
            seed.AdmissionHolds.Add(hold);
            await seed.SaveChangesAsync();
        }

        var clock = new FixedDatabaseTimeInterceptor(now);
        await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(database.ConnectionString).AddInterceptors(clock).Options);
        var details = await Service(context).GetByIdAsync(hold.Id, CancellationToken.None);

        Assert.NotNull(details);
        Assert.Equal(expectedStatus, details.Status);
        Assert.Equal(now, details.ServerTime);
        Assert.Equal(hold.ExpiresAt, details.ExpiresAt);
        Assert.Equal(1, clock.ClockReads);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [PostgreSqlSchemaFact]
    public async Task ExistingTransactionUsesFreshWallClockAndLeavesStoredHeldStatusUntouched()
    {
        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var transactionStart = await context.Database.SqlQuery<DateTime>($"SELECT CURRENT_TIMESTAMP AS \"Value\"").SingleAsync();
        var expiresAt = await ReadTimeAsync(context);
        Assert.True(expiresAt > transactionStart);
        var hold = NewHold(AdmissionHoldStatus.HELD, expiresAt);
        context.AdmissionHolds.Add(hold);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var details = await Service(context).GetByIdAsync(hold.Id, CancellationToken.None);

        Assert.NotNull(details);
        Assert.Equal("EXPIRED", details.Status);
        Assert.True(details.ServerTime >= expiresAt);
        Assert.Equal(expiresAt, details.ExpiresAt);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Equal(AdmissionHoldStatus.HELD, (await context.AdmissionHolds.AsNoTracking().SingleAsync()).Status);
        await transaction.RollbackAsync();
    }

    [PostgreSqlSchemaFact]
    public async Task UnknownHoldReturns404AndMalformedIdReturns400()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var path = $"/holds/{Guid.NewGuid()}";
        using var missing = await client.GetAsync(path);
        await AssertProblemAsync(missing, HttpStatusCode.NotFound, path);
        using var malformed = await client.GetAsync("/holds/not-a-uuid");
        await AssertProblemAsync(malformed, HttpStatusCode.BadRequest, "/holds/not-a-uuid");
    }

    [PostgreSqlSchemaFact]
    public async Task CreatedHoldCanBeRetrievedWithoutRepeatingTheStudentHeader()
    {
        Guid classId;
        await using (var context = database.CreateContext())
        {
            var admissionClass = new AdmissionClass { Name = "Creation to retrieval", TotalSeats = 1 };
            context.AdmissionClasses.Add(admissionClass);
            await context.SaveChangesAsync();
            classId = admissionClass.Id;
        }

        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/classes/{classId}/holds");
        request.Headers.Add("X-Student-Id", "student-1");
        using var createdResponse = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var created = (await createdResponse.Content.ReadFromJsonAsync<CreateHoldResponse>())!;

        var details = await client.GetFromJsonAsync<HoldDetailsResponse>($"/holds/{created.HoldId}");

        Assert.NotNull(details);
        Assert.Equal(created.HoldId, details.HoldId);
        Assert.Equal(classId, details.ClassId);
        Assert.Equal(created.ExpiresAt, details.ExpiresAt);
        Assert.Equal("HELD", details.Status);
        Assert.True(details.ServerTime < details.ExpiresAt);
    }

    [PostgreSqlSchemaFact]
    public async Task SuppliedStudentHeaderDoesNotRestrictThePublicStatusEndpoint()
    {
        AdmissionHold hold;
        await using (var seed = database.CreateContext())
        {
            hold = NewHold(AdmissionHoldStatus.HELD, (await ReadTimeAsync(seed)).AddHours(1));
            seed.AdmissionHolds.Add(hold);
            await seed.SaveChangesAsync();
        }

        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Student-Id", "different-student");
        using var response = await client.GetAsync($"/holds/{hold.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(body.RootElement.TryGetProperty("studentId", out _));
    }

    [PostgreSqlSchemaFact]
    public async Task OpenApiDocumentsRetrievalWithoutAStudentHeader()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        var operation = document.RootElement.GetProperty("paths").GetProperty("/holds/{holdId}").GetProperty("get");
        var parameter = Assert.Single(operation.GetProperty("parameters").EnumerateArray());
        Assert.Equal("holdId", parameter.GetProperty("name").GetString());
        Assert.Equal("path", parameter.GetProperty("in").GetString());
        foreach (var status in new[] { "200", "400", "404" })
            Assert.True(operation.GetProperty("responses").TryGetProperty(status, out _));
    }

    private FoundationApiFactory CreateFactory() => new(overrides: new Dictionary<string, string?>
    {
        ["ConnectionStrings:DefaultConnection"] = database.ConnectionString
    });

    private static HoldService Service(AppDbContext context) => new(context, Options.Create(new HoldOptions()));

    private static AdmissionHold NewHold(AdmissionHoldStatus status, DateTime expiresAt)
    {
        var admissionClass = new AdmissionClass { Name = "Status lookup", TotalSeats = 1 };
        return new AdmissionHold
        {
            AdmissionClass = admissionClass,
            ClassId = admissionClass.Id,
            StudentId = "student-1",
            Status = status,
            ExpiresAt = expiresAt,
            ConfirmedAt = status == AdmissionHoldStatus.CONFIRMED ? expiresAt.AddHours(-2) : null,
            CancelledAt = status == AdmissionHoldStatus.CANCELLED ? expiresAt.AddHours(-2) : null
        };
    }

    private static Task<DateTime> ReadTimeAsync(AppDbContext context) =>
        context.Database.SqlQuery<DateTime>($"SELECT clock_timestamp() AS \"Value\"").SingleAsync();

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode expected, string path)
    {
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal((int)expected, body.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(path, body.RootElement.GetProperty("instance").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("traceId").GetString()));
    }
}
