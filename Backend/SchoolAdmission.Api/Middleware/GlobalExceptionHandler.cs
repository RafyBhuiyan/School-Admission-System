using Microsoft.AspNetCore.Diagnostics;
using SchoolAdmission.Api.Common;

namespace SchoolAdmission.Api.Middleware;

public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var statusCode = exception is BadHttpRequestException badRequest
            ? badRequest.StatusCode
            : StatusCodes.Status500InternalServerError;

        if (statusCode >= StatusCodes.Status500InternalServerError)
        {
            // Exception messages can contain database values or personal identifiers.
            logger.LogError("Unhandled {ExceptionType}. Request trace ID: {TraceId}",
                exception.GetType().FullName, httpContext.TraceIdentifier);
        }

        var problem = ApiProblemDetails.Create(httpContext, statusCode,
            statusCode >= StatusCodes.Status500InternalServerError
                ? "An unexpected error occurred. Use the trace ID when reporting this problem."
                : "The request could not be processed. Check the request format and try again.");

        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(problem, options: null,
            contentType: "application/problem+json", cancellationToken: cancellationToken);
        return true;
    }
}
