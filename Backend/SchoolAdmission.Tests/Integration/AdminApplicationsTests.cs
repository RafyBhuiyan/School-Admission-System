using System.Net;
using System.Text.Json;
using SchoolAdmission.Api.Enums;
using SchoolAdmission.Api.Models.Entities;

namespace SchoolAdmission.Tests.Integration;

public sealed class AdminApplicationsTests(PostgreSqlSchemaFixture database) : IClassFixture<PostgreSqlSchemaFixture>
{
    [Fact]
    public async Task AnonymousUsersCannotReadApplications()
    {
        using var factory = new FoundationApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Remove("X-Admin-Key");
        using var response = await client.GetAsync("/admin/applications");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("status=unknown")]
    [InlineData("status=1")]
    [InlineData("page=0")]
    [InlineData("pageSize=101")]
    public async Task InvalidFiltersAreRejected(string query)
    {
        using var factory = new FoundationApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/admin/applications?" + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [PostgreSqlSchemaFact]
    public async Task AuthorizedListFiltersEffectiveStatusesAndPaginates()
    {
        await using var context = database.CreateContext();
        var morning = new AdmissionClass { Name = "Morning", TotalSeats = 10 };
        var day = new AdmissionClass { Name = "Day", TotalSeats = 10 };
        var expired = new AdmissionHold { AdmissionClass = morning, ClassId = morning.Id,
            StudentId = "synthetic-expired", Status = AdmissionHoldStatus.HELD, ExpiresAt = DateTime.UtcNow.AddMinutes(-5) };
        context.AdmissionHolds.AddRange(expired,
            new AdmissionHold { AdmissionClass = morning, ClassId = morning.Id, StudentId = "synthetic-held",
                Status = AdmissionHoldStatus.HELD, ExpiresAt = DateTime.UtcNow.AddMinutes(10) },
            new AdmissionHold { AdmissionClass = day, ClassId = day.Id, StudentId = "synthetic-confirmed",
                Status = AdmissionHoldStatus.CONFIRMED, ConfirmedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddMinutes(10) });
        await context.SaveChangesAsync();
        using var factory = new FoundationApiFactory(overrides: new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = database.ConnectionString });
        using var client = factory.CreateClient();
        using var response = await client.GetAsync($"/admin/applications?classId={morning.Id}&status=EXPIRED");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var data = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(1, data.RootElement.GetProperty("total").GetInt32());
        var item = Assert.Single(data.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(expired.Id, item.GetProperty("holdId").GetGuid());
        Assert.Equal("EXPIRED", item.GetProperty("status").GetString());
        Assert.Equal("synthetic-expired", item.GetProperty("studentId").GetString());
        using var first = JsonDocument.Parse(await client.GetStringAsync("/admin/applications?pageSize=1"));
        using var second = JsonDocument.Parse(await client.GetStringAsync("/admin/applications?pageSize=1&page=2"));
        Assert.Equal(3, first.RootElement.GetProperty("total").GetInt32());
        Assert.NotEqual(first.RootElement.GetProperty("items")[0].GetProperty("holdId").GetString(), second.RootElement.GetProperty("items")[0].GetProperty("holdId").GetString());
        using var held = JsonDocument.Parse(await client.GetStringAsync($"/admin/applications?classId={morning.Id}&status=HELD"));
        Assert.Equal(1, held.RootElement.GetProperty("total").GetInt32());
    }
}
