using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace JobPosting.Api.Diagnostics;

public sealed class UnexpectedExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<UnexpectedExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        logger.LogError(new EventId(1001, "UnhandledException"), exception,
            "Unhandled request exception; trace {TraceId}", httpContext.TraceIdentifier);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "An unexpected error occurred.",
            Detail = "The request could not be completed. Contact support with the trace identifier.",
            Type = "https://www.rfc-editor.org/rfc/rfc9110.html#section-15.6.1"
        };
        problem.Extensions["traceId"] = httpContext.TraceIdentifier;

        if (!await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem
        }))
        {
            // Still return safe JSON if the caller's Accept header excludes the default writer.
            await httpContext.Response.WriteAsJsonAsync(problem,
                options: (System.Text.Json.JsonSerializerOptions?)null,
                contentType: "application/problem+json", cancellationToken: cancellationToken);
        }

        return true;
    }
}
