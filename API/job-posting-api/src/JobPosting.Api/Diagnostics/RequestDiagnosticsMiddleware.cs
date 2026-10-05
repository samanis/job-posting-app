using System.Diagnostics;
using Microsoft.AspNetCore.Routing;

namespace JobPosting.Api.Diagnostics;

public sealed class RequestDiagnosticsMiddleware(RequestDelegate next, ILogger<RequestDiagnosticsMiddleware> logger)
{
    public const string TraceHeader = "X-Trace-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        context.TraceIdentifier = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
        var traceId = context.TraceIdentifier;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[TraceHeader] = traceId;
            return Task.CompletedTask;
        });

        using var scope = logger.BeginScope(new Dictionary<string, object> { ["TraceId"] = traceId });
        var started = Stopwatch.GetTimestamp();

        try
        {
            await next(context);
        }
        finally
        {
            // Use the route template, never arbitrary URLs, query strings, headers or bodies.
            var route = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "unmatched";
            PostingMetrics.Request(context.Response.StatusCode, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            logger.LogInformation(new EventId(1000, "RequestCompleted"),
                "HTTP {Method} {Route} completed with {StatusCode} in {ElapsedMilliseconds} ms; trace {TraceId}",
                context.Request.Method, route, context.Response.StatusCode,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds, traceId);
        }
    }
}
