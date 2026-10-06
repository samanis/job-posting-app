using System.Diagnostics;
using System.Text.Json;
using JobSearch.Api.Diagnostics;
using JobSearch.Api.Tests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace JobSearch.Api.Tests;

[Trait("Category", "Unit")]
public sealed class DiagnosticsTests
{
    [Theory]
    [InlineData(false, "none")]
    [InlineData(true, "template")]
    [InlineData(false, "no-raw-text")]
    public async Task RequestLogsUseTraceAndRouteTemplateAndSetResponseHeader(bool activeTrace, string route)
    {
        var previous = Activity.Current;
        Activity.Current = null;
        using var activity = activeTrace ? new Activity("unit-request").SetIdFormat(ActivityIdFormat.W3C).Start() : null;
        try
        {
            var context = new DefaultHttpContext { TraceIdentifier = "server-trace" };
            var response = new StartingResponseFeature();
            context.Features.Set<IHttpResponseFeature>(response);
            context.Request.Method = "POST";
            context.Request.Path = "/private-value";
            context.Request.QueryString = new QueryString("?secret=private-value");
            if (route != "none")
            {
                var pattern = route == "template" ? RoutePatternFactory.Parse("jobs/{id}") : RoutePatternFactory.Pattern();
                context.SetEndpoint(new RouteEndpoint(_ => Task.CompletedTask, pattern, 0, EndpointMetadataCollection.Empty, "unit"));
            }
            using var logs = new CapturedLoggerProvider();
            using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(logs));
            var middleware = new RequestDiagnosticsMiddleware(async http =>
            {
                http.Response.StatusCode = 204;
                await response.StartAsync();
            }, loggerFactory.CreateLogger<RequestDiagnosticsMiddleware>());
            await middleware.InvokeAsync(context);

            var trace = activity?.TraceId.ToString() ?? "server-trace";
            Assert.Equal(trace, context.TraceIdentifier);
            Assert.Equal(trace, context.Response.Headers[RequestDiagnosticsMiddleware.TraceHeader]);
            var entry = Assert.Single(logs.Entries);
            Assert.Equal(1000, entry.EventId.Id);
            Assert.Equal("POST", entry.Properties["Method"]);
            Assert.Equal(route == "template" ? "jobs/{id}" : "unmatched", entry.Properties["Route"]);
            Assert.Equal(204, entry.Properties["StatusCode"]);
            Assert.Equal(trace, entry.Properties["TraceId"]);
            Assert.True(Assert.IsType<double>(entry.Properties["ElapsedMilliseconds"]) >= 0);
            Assert.DoesNotContain("private-value", JsonSerializer.Serialize(entry.Properties));
        }
        finally
        {
            activity?.Stop();
            Activity.Current = previous;
        }
    }

    [Fact]
    public async Task RequestCompletionIsLoggedWhenNextDelegateThrows()
    {
        using var logs = new CapturedLoggerProvider();
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(logs));
        var exception = new InvalidOperationException("private-error");
        var middleware = new RequestDiagnosticsMiddleware(_ => throw exception,
            factory.CreateLogger<RequestDiagnosticsMiddleware>());
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(new DefaultHttpContext()));
        Assert.Same(exception, thrown);
        Assert.Equal(1000, Assert.Single(logs.Entries).EventId.Id);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExceptionHandlerUsesSafeProblemWriterOrJsonFallback(bool writerAccepts)
    {
        using var services = new ServiceCollection().AddLogging().AddOptions().BuildServiceProvider();
        var context = new DefaultHttpContext { TraceIdentifier = "test-trace", RequestServices = services };
        using var body = new MemoryStream();
        context.Response.Body = body;
        using var logs = new CapturedLoggerProvider();
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(logs));
        var writer = new ProblemWriter(writerAccepts);
        var handler = new UnexpectedExceptionHandler(writer, factory.CreateLogger<UnexpectedExceptionHandler>());
        var exception = new InvalidOperationException("private-error");
        using var cancellation = new CancellationTokenSource();
        Assert.True(await handler.TryHandleAsync(context, exception, cancellation.Token));
        Assert.Equal(500, context.Response.StatusCode);
        var problem = Assert.IsType<ProblemDetailsContext>(writer.Context).ProblemDetails;
        Assert.Equal(500, problem.Status);
        Assert.Equal("An unexpected error occurred.", problem.Title);
        Assert.Equal("test-trace", problem.Extensions["traceId"]);
        Assert.DoesNotContain("private-error", JsonSerializer.Serialize(problem));
        if (!writerAccepts)
        {
            Assert.Equal("application/problem+json", context.Response.ContentType);
            body.Position = 0;
            using var json = await JsonDocument.ParseAsync(body);
            Assert.Equal(500, json.RootElement.GetProperty("status").GetInt32());
            Assert.Equal("test-trace", json.RootElement.GetProperty("traceId").GetString());
        }
        else
        {
            Assert.Equal(0, body.Length);
        }
        var log = Assert.Single(logs.Entries);
        Assert.Equal(1001, log.EventId.Id);
        Assert.Equal(LogLevel.Error, log.Level);
        Assert.Null(log.Exception);
        Assert.Equal("test-trace", log.Properties["TraceId"]);
    }

    private sealed class ProblemWriter(bool accepts) : IProblemDetailsService
    {
        public ProblemDetailsContext? Context { get; private set; }
        public ValueTask<bool> TryWriteAsync(ProblemDetailsContext context)
        {
            Context = context;
            return ValueTask.FromResult(accepts);
        }
        public ValueTask WriteAsync(ProblemDetailsContext context) => throw new NotSupportedException();
    }

    private sealed class StartingResponseFeature : HttpResponseFeature
    {
        private readonly Stack<(Func<object, Task> Callback, object State)> callbacks = new();
        public override void OnStarting(Func<object, Task> callback, object state) => callbacks.Push((callback, state));
        public async Task StartAsync()
        {
            while (callbacks.TryPop(out var item))
            {
                await item.Callback(item.State);
            }
        }
    }
}
