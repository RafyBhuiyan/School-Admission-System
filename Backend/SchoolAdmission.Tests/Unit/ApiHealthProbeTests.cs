using Microsoft.Extensions.Configuration;
using SchoolAdmission.Api.Common;

namespace SchoolAdmission.Tests.Unit;

public sealed class ApiHealthProbeTests
{
    private static IConfiguration Settings(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(value => value.Key, value => value.Value)).Build();

    [Theory]
    [InlineData("http://0.0.0.0:9123", "http://127.0.0.1:9123/")]
    [InlineData("http://+:9124", "http://127.0.0.1:9124/")]
    [InlineData("http://*:9125", "http://127.0.0.1:9125/")]
    [InlineData("http://[::]:9126", "http://[::1]:9126/")]
    [InlineData("http://localhost:9127", "http://localhost:9127/")]
    [InlineData("http://192.0.2.1:9128", "http://192.0.2.1:9128/")]
    [InlineData("http://binding.example:9129", "http://127.0.0.1:9129/")]
    [InlineData("HTTP://LOCALHOST:9130", "http://localhost:9130/")]
    public void UsesConfiguredListenerAndNormalizesWildcardHosts(string listener, string expected)
    {
        Assert.Equal(expected, ApiHealthProbe.ResolveBaseAddress(Settings(("urls", listener))).AbsoluteUri);
    }

    [Fact]
    public void UrlsOverridePortSettingsAndPreferAnAvailableHttpListener()
    {
        var configuration = Settings(("urls", "https://localhost:9443;http://localhost:9123"), ("http_ports", "9999"));
        Assert.Equal("http://localhost:9123/", ApiHealthProbe.ResolveBaseAddress(configuration).AbsoluteUri);
    }

    [Fact]
    public void ExplicitKestrelEndpointsOverrideUrlsUnlessHostingUrlsArePreferred()
    {
        var configuration = Settings(("Kestrel:Endpoints:Http:Url", "http://0.0.0.0:9210"), ("urls", "http://localhost:9211"));
        Assert.Equal(9210, ApiHealthProbe.ResolveBaseAddress(configuration).Port);
        configuration["preferHostingUrls"] = "true";
        Assert.Equal(9211, ApiHealthProbe.ResolveBaseAddress(configuration).Port);
        configuration["urls"] = null;
        configuration["http_ports"] = "9215";
        Assert.Equal(9215, ApiHealthProbe.ResolveBaseAddress(configuration).Port);
    }

    [Theory]
    [InlineData("http_ports", "9212;9213", "http://127.0.0.1:9212/")]
    [InlineData("https_ports", "9444", "https://127.0.0.1:9444/")]
    public void UsesConfiguredPortsWithoutAFixedPort(string key, string ports, string expected)
    {
        Assert.Equal(expected, ApiHealthProbe.ResolveBaseAddress(Settings((key, ports))).AbsoluteUri);
    }

    [Fact]
    public void BrowserFacingApiAddressDoesNotReplaceTheInternalListener()
    {
        var configuration = Settings(("API_BASE_URL", "https://public.example/school"), ("http_ports", "9321"));
        Assert.Equal("http://127.0.0.1:9321/", ApiHealthProbe.ResolveBaseAddress(configuration).AbsoluteUri);
    }

    [Fact]
    public void ExplicitProbeOverrideRetainsAPathPrefixForRelativeRequests()
    {
        var configuration = Settings(("HealthCheck:BaseUrl", "https://internal.example:9445/school"), ("http_ports", "9321"));
        var baseAddress = ApiHealthProbe.ResolveBaseAddress(configuration);
        Assert.Equal("https://internal.example:9445/school/classes", new Uri(baseAddress, "classes").AbsoluteUri);
    }

    [Theory]
    [InlineData("relative/path")]
    [InlineData("file:///tmp/app")]
    [InlineData("http://user:synthetic-secret@internal.example")]
    [InlineData("http://internal.example?token=synthetic-secret")]
    [InlineData("http://internal.example#fragment")]
    public void RejectsUnsafeOverridesWithoutExposingTheirValues(string value)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => ApiHealthProbe.ResolveBaseAddress(Settings(("HealthCheck:BaseUrl", value))));
        Assert.DoesNotContain(value, exception.Message);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("65536")]
    [InlineData("invalid")]
    public void InvalidListenerPortsFailClosed(string value)
    {
        Assert.Throws<InvalidOperationException>(() => ApiHealthProbe.ResolveBaseAddress(Settings(("http_ports", value))));
    }

    [Fact]
    public void MissingListenerSettingsDoNotIntroduceAnImplicitAddress()
    {
        Assert.Throws<InvalidOperationException>(() => ApiHealthProbe.ResolveBaseAddress(Settings()));
    }
}
