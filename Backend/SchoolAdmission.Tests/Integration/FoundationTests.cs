using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SchoolAdmission.Api.Data;

namespace SchoolAdmission.Tests.Integration;

public sealed class FoundationTests
{
    [Theory]
    [InlineData("Expiration:IntervalMilliseconds", "0")]
    [InlineData("Expiration:IntervalMilliseconds", "99")]
    [InlineData("Expiration:IntervalMilliseconds", "60001")]
    [InlineData("Expiration:BatchSize", "0")]
    [InlineData("Expiration:BatchSize", "-1")]
    [InlineData("Expiration:BatchSize", "1001")]
    public void InvalidExpirationConfigurationFailsStartup(string key, string value)
    {
        using var factory = new FoundationApiFactory(overrides: new Dictionary<string, string?> { [key] = value });
        var exception = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
        Assert.Contains(key, exception.Message);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("")]
    [InlineData("not-a-number")]
    [InlineData("1.5")]
    [InlineData("2147483648")]
    public void InvalidHoldDurationFailsStartup(string value)
    {
        using var factory = new FoundationApiFactory(overrides: new Dictionary<string, string?>
        {
            ["HOLD_DURATION_SECONDS"] = value
        });
        var exception = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
        Assert.Contains("HOLD_DURATION_SECONDS must be a positive 32-bit integer", exception.Message);
    }

    [Fact]
    public void DbContextUsesPostgreSqlAndIsScopedWithoutOpeningAConnection()
    {
        using var factory = new FoundationApiFactory();
        using var firstScope = factory.Services.CreateScope();
        using var secondScope = factory.Services.CreateScope();
        var first = firstScope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.Same(first, firstScope.ServiceProvider.GetRequiredService<AppDbContext>());
        Assert.NotSame(first, secondScope.ServiceProvider.GetRequiredService<AppDbContext>());
        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", first.Database.ProviderName);
        Assert.Equal("foundation_tests", first.Database.GetDbConnection().Database);
        Assert.Equal(System.Data.ConnectionState.Closed, first.Database.GetDbConnection().State);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Host=localhost")]
    [InlineData("InvalidSetting=synthetic-config-secret")]
    public void InvalidDatabaseConfigurationFailsStartupWithoutExposingItsValue(string connectionString)
    {
        using var factory = new FoundationApiFactory(overrides: new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = connectionString
        });

        var exception = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());

        Assert.Contains("ConnectionStrings__DefaultConnection", exception.Message);
        Assert.DoesNotContain("synthetic-config-secret", exception.ToString());
    }

    [Theory]
    [InlineData("*")]
    [InlineData("https://parents.example/app")]
    [InlineData("https://parents.example/")]
    public void InvalidCorsConfigurationFailsStartup(string origin)
    {
        using var factory = new FoundationApiFactory(overrides: new Dictionary<string, string?>
        {
            ["Cors:AllowedOrigins:0"] = origin
        });

        var exception = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());

        Assert.Contains("Cors:AllowedOrigins", exception.Message);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    [InlineData("DELETE")]
    public async Task AllowedOriginCanPreflightStudentRequests(string method)
    {
        using var factory = new FoundationApiFactory();
        using var client = factory.CreateClient();
        using var request = CreatePreflight("http://localhost:5173", method, "content-type,x-student-id");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("http://localhost:5173", response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Contains(method, response.Headers.GetValues("Access-Control-Allow-Methods").Single());
        Assert.Contains("X-Student-Id", response.Headers.GetValues("Access-Control-Allow-Headers").Single(),
            StringComparison.OrdinalIgnoreCase);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
    }

    [Fact]
    public async Task UnlistedOriginDoesNotReceiveCorsPermission()
    {
        using var factory = new FoundationApiFactory();
        using var client = factory.CreateClient();
        using var request = CreatePreflight("https://unlisted.example", "POST", "x-student-id");

        using var response = await client.SendAsync(request);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task ConfiguredFrontendOriginReplacesTheDefault()
    {
        using var factory = new FoundationApiFactory(overrides: new Dictionary<string, string?>
        {
            ["Cors:AllowedOrigins:0"] = "https://parents.example"
        });
        using var client = factory.CreateClient();
        using var allowedRequest = CreatePreflight("https://parents.example", "POST", "x-student-id");
        using var oldRequest = CreatePreflight("http://localhost:5173", "POST", "x-student-id");

        using var allowed = await client.SendAsync(allowedRequest);
        using var old = await client.SendAsync(oldRequest);

        Assert.Equal("https://parents.example", allowed.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.False(old.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task UnexpectedErrorsAreSanitizedProblemDetailsEvenWhenHtmlIsRequested(string environment)
    {
        using var factory = new FoundationApiFactory(environment);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html");

        using var response = await client.GetAsync("/__foundation-tests/error?private=synthetic-query-value");
        var body = await response.Content.ReadAsStringAsync();
        using var problem = AssertProblem(response, body, HttpStatusCode.InternalServerError, "/__foundation-tests/error");

        Assert.DoesNotContain("synthetic-sensitive-detail", body);
        Assert.DoesNotContain("InvalidOperationException", body);
        Assert.DoesNotContain("stack", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("synthetic-query-value", body);
    }

    [Fact]
    public async Task BadHttpRequestsKeepTheirClientErrorStatus()
    {
        using var factory = new FoundationApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/__foundation-tests/bad-request");
        var body = await response.Content.ReadAsStringAsync();
        using var problem = AssertProblem(response, body, HttpStatusCode.BadRequest, "/__foundation-tests/bad-request");
        Assert.DoesNotContain("synthetic-sensitive-detail", body);
    }

    [Fact]
    public async Task InvalidDtoReturnsAutomaticValidationProblemDetails()
    {
        using var factory = new FoundationApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/__foundation-tests/validate", new { name = "", seats = 0 });
        using var problem = AssertProblem(response, await response.Content.ReadAsStringAsync(),
            HttpStatusCode.BadRequest, "/__foundation-tests/validate");

        Assert.True(problem.RootElement.GetProperty("errors").GetProperty("Name").GetArrayLength() > 0);
        Assert.True(problem.RootElement.GetProperty("errors").GetProperty("Seats").GetArrayLength() > 0);
    }

    [Theory]
    [InlineData("{\"name\":")]
    [InlineData("{\"name\":\"test\",\"seats\":\"synthetic-invalid-value\"}")]
    public async Task InvalidJsonReturnsSanitizedValidationProblemDetails(string json)
    {
        using var factory = new FoundationApiFactory();
        using var client = factory.CreateClient();
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await client.PostAsync("/__foundation-tests/validate", content);
        var body = await response.Content.ReadAsStringAsync();
        using var problem = AssertProblem(response, body, HttpStatusCode.BadRequest, "/__foundation-tests/validate");

        Assert.True(problem.RootElement.TryGetProperty("errors", out _));
        Assert.DoesNotContain("synthetic-invalid-value", body);
        Assert.DoesNotContain("JsonException", body);
    }

    [Fact]
    public async Task UnmatchedRouteReturnsProblemDetails()
    {
        using var factory = new FoundationApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/route-that-does-not-exist");
        using var problem = AssertProblem(response, await response.Content.ReadAsStringAsync(),
            HttpStatusCode.NotFound, "/route-that-does-not-exist");
    }

    [Theory]
    [InlineData("Development", HttpStatusCode.OK)]
    [InlineData("Production", HttpStatusCode.NotFound)]
    public async Task SwaggerAndOpenApiAreAvailableOnlyInDevelopment(string environment, HttpStatusCode expected)
    {
        using var factory = new FoundationApiFactory(environment);
        using var client = factory.CreateClient();

        using var swagger = await client.GetAsync("/swagger/index.html");
        using var openApi = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(expected, swagger.StatusCode);
        Assert.Equal(expected, openApi.StatusCode);
        if (expected == HttpStatusCode.OK)
        {
            Assert.Contains("Swagger UI", await swagger.Content.ReadAsStringAsync());
            using var document = JsonDocument.Parse(await openApi.Content.ReadAsStringAsync());
            Assert.StartsWith("3.", document.RootElement.GetProperty("openapi").GetString());
            Assert.DoesNotContain("__foundation-tests", document.RootElement.GetProperty("paths").GetRawText());
        }
    }

    private static HttpRequestMessage CreatePreflight(string origin, string method, string headers)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/__foundation-tests/validate");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", method);
        request.Headers.Add("Access-Control-Request-Headers", headers);
        return request;
    }

    private static JsonDocument AssertProblem(HttpResponseMessage response, string body, HttpStatusCode status, string path)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var document = JsonDocument.Parse(body);
        Assert.Equal((int)status, document.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("title").GetString()));
        Assert.Equal(path, document.RootElement.GetProperty("instance").GetString());
        Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("traceId").GetString()));
        return document;
    }
}
