using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolAdmission.Api.DTOs.Responses;
using SchoolAdmission.Api.Enums;
using SchoolAdmission.Api.Models.Entities;

namespace SchoolAdmission.Tests.Integration;

[Trait("Category", "PostgreSQLClassApi")]
public sealed class ClassApiTests(PostgreSqlSchemaFixture database) : IClassFixture<PostgreSqlSchemaFixture>, IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PostgreSqlSchemaFixture.EnvironmentVariable)))
        {
            return;
        }

        // This fixture owns a uniquely named database; each API case starts with empty tables.
        await using var context = database.CreateContext();
        await context.AdmissionHolds.ExecuteDeleteAsync();
        await context.AdmissionClasses.ExecuteDeleteAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [PostgreSqlSchemaFact]
    public async Task CreateTrimsNameReturnsOnlyContractFieldsAndAWorkingLocation()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/classes", new { name = "  Class 6 - Morning Shift  ", totalSeats = 5 });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertFields(body.RootElement, "id", "name", "totalSeats");
        var id = body.RootElement.GetProperty("id").GetGuid();
        Assert.NotEqual(Guid.Empty, id);
        Assert.Equal("Class 6 - Morning Shift", body.RootElement.GetProperty("name").GetString());
        Assert.Equal(5, body.RootElement.GetProperty("totalSeats").GetInt32());
        Assert.Equal($"/classes/{id}", response.Headers.Location?.AbsolutePath);

        using var detailResponse = await client.GetAsync(response.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        using var details = JsonDocument.Parse(await detailResponse.Content.ReadAsStringAsync());
        AssertSummary(details.RootElement, id, 5, 0, 0, 5);
        await using var context = database.CreateContext();
        Assert.Equal("Class 6 - Morning Shift", (await context.AdmissionClasses.SingleAsync()).Name);
    }

    [PostgreSqlSchemaFact]
    public async Task EmptyClassListReturnsAnEmptyArray()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/classes");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("[]", await response.Content.ReadAsStringAsync());
    }

    [PostgreSqlSchemaFact]
    public async Task ListReturnsIndependentShiftClassesWithAllRequiredFields()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var morning = await CreateAsync(client, "Class 6 - Morning Shift", 4);
        var day = await CreateAsync(client, "Class 6 - Day Shift", 6);

        using var response = await client.GetAsync("/classes");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(2, body.RootElement.GetArrayLength());
        foreach (var row in body.RootElement.EnumerateArray())
        {
            var expected = row.GetProperty("id").GetGuid() == morning.Id ? morning : day;
            AssertSummary(row, expected.Id, expected.TotalSeats, 0, 0, expected.TotalSeats);
            Assert.Equal(expected.Name, row.GetProperty("name").GetString());
        }
    }

    [PostgreSqlSchemaTheory]
    [InlineData("{}")]
    [InlineData("{\"name\":null,\"totalSeats\":1}")]
    [InlineData("{\"name\":\"   \",\"totalSeats\":1}")]
    [InlineData("{\"name\":\"Class\"}")]
    [InlineData("{\"name\":\"Class\",\"totalSeats\":null}")]
    [InlineData("{\"name\":\"Class\",\"totalSeats\":0}")]
    [InlineData("{\"name\":\"Class\",\"totalSeats\":-1}")]
    [InlineData("{\"name\":\"Class\",\"totalSeats\":1.5}")]
    [InlineData("{\"name\":\"Class\",\"totalSeats\":2147483648}")]
    [InlineData("{\"name\":\"Class\",\"totalSeats\":\"5\"}")]
    [InlineData("{\"name\":42,\"totalSeats\":1}")]
    [InlineData("{\"name\":")]
    [InlineData("null")]
    public async Task InvalidCreationReturns400ProblemDetailsWithoutPersistingAClass(string json)
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await client.PostAsync("/classes", content);

        using var problem = await AssertProblemAsync(response, HttpStatusCode.BadRequest, "/classes");
        Assert.True(problem.RootElement.TryGetProperty("errors", out _));
        await using var context = database.CreateContext();
        Assert.Equal(0, await context.AdmissionClasses.CountAsync());
    }

    [PostgreSqlSchemaFact]
    public async Task NameLengthIsCheckedAfterTrimmingAndMaximumCapacityIsAccepted()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var created = await CreateAsync(client, $" \t{new string('A', 200)}\r\n ", int.MaxValue);

        Assert.Equal(200, created.Name.Length);
        using var response = await client.GetAsync($"/classes/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertSummary(body.RootElement, created.Id, int.MaxValue, 0, 0, int.MaxValue);
    }

    [PostgreSqlSchemaFact]
    public async Task OverlongNameReturns400()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/classes", new { name = new string('A', 201), totalSeats = 1 });

        using var problem = await AssertProblemAsync(response, HttpStatusCode.BadRequest, "/classes");
        Assert.True(problem.RootElement.GetProperty("errors").GetProperty("Name").GetArrayLength() > 0);
    }

    [PostgreSqlSchemaFact]
    public async Task DuplicateTrimmedNameReturns409WhileDifferentCaseIsAllowed()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await CreateAsync(client, "Class 6", 5);

        using var duplicate = await client.PostAsJsonAsync("/classes", new { name = "  Class 6  ", totalSeats = 6 });
        using var problem = await AssertProblemAsync(duplicate, HttpStatusCode.Conflict, "/classes");
        Assert.DoesNotContain("ux_admission_classes_name", problem.RootElement.GetRawText());
        await CreateAsync(client, "class 6", 6);
        await using var context = database.CreateContext();
        Assert.Equal(2, await context.AdmissionClasses.CountAsync());
    }

    [PostgreSqlSchemaFact]
    public async Task RacingDuplicateNamesCreateExactlyOneClassAndReturnConflictsForTheRest()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var responses = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => client.PostAsJsonAsync("/classes", new { name = "Same class name", totalSeats = 3 })));
        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
            Assert.Equal(7, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
            await using var context = database.CreateContext();
            Assert.Equal(1, await context.AdmissionClasses.CountAsync());
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [PostgreSqlSchemaFact]
    public async Task UnknownClassReturns404AndMalformedIdReturns400()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var missingPath = $"/classes/{Guid.NewGuid()}";

        using var missing = await client.GetAsync(missingPath);
        using var missingProblem = await AssertProblemAsync(missing, HttpStatusCode.NotFound, missingPath);
        using var invalid = await client.GetAsync("/classes/not-a-uuid");
        using var invalidProblem = await AssertProblemAsync(invalid, HttpStatusCode.BadRequest, "/classes/not-a-uuid");
    }

    [PostgreSqlSchemaFact]
    public async Task ListAndDetailsCountOnlyActiveHoldsAndConfirmedAdmissions()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var created = await CreateAsync(client, "Seat summary class", 5);
        await using (var context = database.CreateContext())
        {
            var databaseTime = await context.Database.SqlQuery<DateTime>($"SELECT clock_timestamp() AS \"Value\"").SingleAsync();
            context.AdmissionHolds.AddRange(
                Hold(created.Id, "synthetic-active", AdmissionHoldStatus.HELD, databaseTime.AddHours(1)),
                Hold(created.Id, "synthetic-overdue", AdmissionHoldStatus.HELD, databaseTime.AddHours(-1)),
                Hold(created.Id, "synthetic-confirmed", AdmissionHoldStatus.CONFIRMED, databaseTime.AddHours(-1)),
                Hold(created.Id, "synthetic-expired", AdmissionHoldStatus.EXPIRED, databaseTime.AddHours(1)),
                Hold(created.Id, "synthetic-cancelled", AdmissionHoldStatus.CANCELLED, databaseTime.AddHours(1)));
            await context.SaveChangesAsync();
        }

        using var listResponse = await client.GetAsync("/classes");
        using var detailResponse = await client.GetAsync($"/classes/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        using var list = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
        using var details = JsonDocument.Parse(await detailResponse.Content.ReadAsStringAsync());
        AssertSummary(list.RootElement[0], created.Id, 5, 1, 1, 3);
        AssertSummary(details.RootElement, created.Id, 5, 1, 1, 3);
    }

    [PostgreSqlSchemaFact]
    public async Task OpenApiDocumentsTheThreeClassOperations()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");
        Assert.True(paths.GetProperty("/classes").TryGetProperty("post", out _));
        Assert.True(paths.GetProperty("/classes").TryGetProperty("get", out _));
        Assert.True(paths.GetProperty("/classes/{classId}").TryGetProperty("get", out _));
    }

    private FoundationApiFactory CreateFactory() => new(overrides: new Dictionary<string, string?>
    {
        ["ConnectionStrings:DefaultConnection"] = database.ConnectionString
    });

    private static async Task<CreateClassResponse> CreateAsync(HttpClient client, string name, int totalSeats)
    {
        using var response = await client.PostAsJsonAsync("/classes", new { name, totalSeats });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CreateClassResponse>())!;
    }

    private static AdmissionHold Hold(Guid classId, string studentId, AdmissionHoldStatus status, DateTime expiresAt) =>
        new() { ClassId = classId, StudentId = studentId, Status = status, ExpiresAt = expiresAt };

    private static void AssertSummary(JsonElement body, Guid id, int total, int held, int confirmed, int available)
    {
        AssertFields(body, "id", "name", "totalSeats", "heldSeats", "confirmedSeats", "availableSeats");
        Assert.Equal(id, body.GetProperty("id").GetGuid());
        Assert.Equal(total, body.GetProperty("totalSeats").GetInt32());
        Assert.Equal(held, body.GetProperty("heldSeats").GetInt32());
        Assert.Equal(confirmed, body.GetProperty("confirmedSeats").GetInt32());
        Assert.Equal(available, body.GetProperty("availableSeats").GetInt32());
    }

    private static void AssertFields(JsonElement body, params string[] names) =>
        Assert.Equal(names.Order(), body.EnumerateObject().Select(property => property.Name).Order());

    private static async Task<JsonDocument> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string path)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal((int)status, document.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(path, document.RootElement.GetProperty("instance").GetString());
        Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("traceId").GetString()));
        return document;
    }
}
