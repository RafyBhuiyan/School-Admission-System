using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace SchoolAdmission.Api.Common;

public static class ApiProblemDetails
{
    public static ProblemDetails Create(HttpContext context, int statusCode, string? detail = null)
    {
        var problem = new ProblemDetails
        {
            Type = "about:blank",
            Title = ReasonPhrases.GetReasonPhrase(statusCode),
            Status = statusCode,
            Detail = detail
        };
        Enrich(problem, context);
        return problem;
    }

    public static void Enrich(ProblemDetails problem, HttpContext context)
    {
        problem.Instance ??= context.Request.Path;
        problem.Extensions["traceId"] = context.TraceIdentifier;
    }
}
