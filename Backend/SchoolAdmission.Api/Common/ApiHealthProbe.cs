using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Http;

namespace SchoolAdmission.Api.Common;

public static class ApiHealthProbe
{
    public static Uri ResolveBaseAddress(IConfiguration configuration)
    {
        if (!string.IsNullOrWhiteSpace(configuration["HealthCheck:BaseUrl"]))
        {
            if (!Uri.TryCreate(configuration["HealthCheck:BaseUrl"], UriKind.Absolute, out var configured)
                || configured.Scheme is not ("http" or "https")
                || !string.IsNullOrEmpty(configured.UserInfo + configured.Query + configured.Fragment))
                throw new InvalidOperationException("HealthCheck:BaseUrl must be an absolute HTTP(S) base address without credentials, query, or fragment.");
            return new Uri(configured.AbsoluteUri.TrimEnd('/') + "/");
        }

        var urls = Split(configuration["urls"]);
        var endpoints = configuration.GetSection("Kestrel:Endpoints").GetChildren()
            .Select(endpoint => endpoint["Url"]).Where(url => !string.IsNullOrWhiteSpace(url)).Cast<string>().ToArray();
        var preferUrls = bool.TryParse(configuration["preferHostingUrls"], out var prefer) && prefer;
        var hasHostingBindings = urls.Length > 0 || Split(configuration["http_ports"]).Length > 0 || Split(configuration["https_ports"]).Length > 0;
        var addresses = endpoints.Length > 0 && !(preferUrls && hasHostingBindings) ? endpoints : urls;
        if (addresses.Length > 0)
            return addresses.Select(FromBinding).OrderBy(uri => uri.Scheme == Uri.UriSchemeHttp ? 0 : 1).First();

        foreach (var (key, scheme) in new[] { ("http_ports", Uri.UriSchemeHttp), ("https_ports", Uri.UriSchemeHttps) })
        {
            var ports = Split(configuration[key]);
            if (ports.Length == 0) continue;
            if (!int.TryParse(ports[0], NumberStyles.None, CultureInfo.InvariantCulture, out var port) || port is < 1 or > 65535)
                throw new InvalidOperationException("Configure a valid listener port for the health probe.");
            return new UriBuilder(scheme, IPAddress.Loopback.ToString(), port).Uri;
        }
        throw new InvalidOperationException("Configure the API listener or HealthCheck:BaseUrl before running the health probe.");
    }

    private static string[] Split(string? value) => (value ?? string.Empty)
        .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static Uri FromBinding(string value)
    {
        var binding = BindingAddress.Parse(value);
        if ((!string.Equals(binding.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
             && !string.Equals(binding.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            || binding.Port is < 1 or > 65535 || !string.IsNullOrEmpty(binding.PathBase))
            throw new InvalidOperationException("Configure an HTTP(S) listener or an explicit health-probe base address.");
        var host = binding.Host;
        if (IPAddress.TryParse(host.Trim('[', ']'), out var address))
            host = address.Equals(IPAddress.Any) ? IPAddress.Loopback.ToString()
                : address.Equals(IPAddress.IPv6Any) ? IPAddress.IPv6Loopback.ToString() : address.ToString();
        else if (!string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
            host = IPAddress.Loopback.ToString(); // Kestrel treats non-IP/non-localhost binding names as wildcard hosts.
        return new UriBuilder(binding.Scheme, host, binding.Port).Uri;
    }
}
