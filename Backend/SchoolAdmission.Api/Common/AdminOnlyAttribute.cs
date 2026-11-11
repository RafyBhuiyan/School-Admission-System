using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace SchoolAdmission.Api.Common;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AdminOnlyAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var http = context.HttpContext;
        var configured = http.RequestServices.GetRequiredService<IConfiguration>()["Admin:AccessKey"];
        var supplied = http.Request.Headers["X-Admin-Key"];
        var available = !string.IsNullOrWhiteSpace(configured) && configured.Length >= 32;
        if (available && supplied.Count == 1 && supplied[0] is { Length: <= 512 } value &&
            CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(configured!)),
                SHA256.HashData(Encoding.UTF8.GetBytes(value)))) return;

        var status = available ? StatusCodes.Status401Unauthorized : StatusCodes.Status503ServiceUnavailable;
        context.Result = new ObjectResult(ApiProblemDetails.Create(http, status,
            available ? "Administrator access is required." : "Administrator access is not configured."))
        { StatusCode = status, ContentTypes = { "application/problem+json" } };
    }
}
