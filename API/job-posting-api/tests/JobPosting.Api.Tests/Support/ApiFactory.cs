using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace JobPosting.Api.Tests.Support;

public sealed class ApiFactory(
    string environment = "Production",
    Dictionary<string, string?>? settings = null,
    bool includeTestEndpoints = false,
    TimeProvider? clock = null) : WebApplicationFactory<Program>
{
    public CapturedLoggerProvider Logs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            if (settings is not null)
            {
                configuration.AddInMemoryCollection(settings);
            }
        });
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<ILoggerProvider>(Logs);
            if (clock is not null)
            {
                services.AddSingleton(clock);
            }
            if (includeTestEndpoints)
            {
                services.AddControllers().AddApplicationPart(typeof(TestEndpointsController).Assembly);
            }
        });
    }
}

// Loaded only by factories explicitly opting in; never registered in the deployed API.
[ApiController]
[Route("__tests")]
public sealed class TestEndpointsController : ControllerBase
{
    public const string ExceptionMarker = "internal-database.example: simulated diagnostic detail";

    [HttpGet("fault")]
    public IActionResult Fault() => throw new InvalidOperationException(ExceptionMarker);

    [HttpPost("complete")]
    public IActionResult Complete() => NoContent();
}

public sealed record CapturedLog(
    string Category, EventId EventId, LogLevel Level,
    IReadOnlyDictionary<string, object?> Properties, Exception? Exception);

public sealed class CapturedLoggerProvider : ILoggerProvider
{
    public ConcurrentQueue<CapturedLog> Entries { get; } = new();

    public ILogger CreateLogger(string categoryName) => new CapturedLogger(categoryName, Entries);

    public void Dispose() { }

    private sealed class CapturedLogger(string category, ConcurrentQueue<CapturedLog> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var properties = state is IEnumerable<KeyValuePair<string, object?>> values
                ? values.ToDictionary(pair => pair.Key, pair => pair.Value)
                : new Dictionary<string, object?>();
            entries.Enqueue(new CapturedLog(category, eventId, logLevel, properties, exception));
        }
    }
}
