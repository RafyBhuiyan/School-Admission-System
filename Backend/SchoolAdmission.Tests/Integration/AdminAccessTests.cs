using System.Net;
using System.Net.Http.Json;

namespace SchoolAdmission.Tests.Integration;

public sealed class AdminAccessTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("wrong-key")]
    public async Task ClassCreationRejectsMissingOrInvalidAdministrator(string? key)
    {
        using var factory = new FoundationApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Remove("X-Admin-Key");
        if (key is not null) client.DefaultRequestHeaders.Add("X-Admin-Key", key);
        using var response = await client.PostAsJsonAsync("/classes", new { name = "Blocked", totalSeats = 1 });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ValidAdministratorCanVerifyAccess()
    {
        using var factory = new FoundationApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/admin/session");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task MissingConfigurationFailsClosed()
    {
        using var factory = new FoundationApiFactory(overrides: new Dictionary<string, string?> { ["Admin:AccessKey"] = "" });
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/classes", new { name = "Blocked", totalSeats = 1 });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }
}
