using System.Net;
using System.Text;
using System.Text.Json;
using JobPosting.Api.Configuration;
using JobPosting.Api.Diagnostics;
using JobPosting.Api.Tests.Support;
using JobPosting.Api.Validation;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JobPosting.Api.Tests;

[Trait("Category", "Host")]
public sealed class FoundationTests
{
    [Fact]
    public async Task JobPostIsNotImplementedAndCannotReturnAcceptance()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsync("/api/jobs", new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(404, body.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(response.Headers.GetValues(RequestDiagnosticsMiddleware.TraceHeader).Single(),
            body.RootElement.GetProperty("traceId").GetString());
    }

    [Fact]
    public async Task DevelopmentExposesARealOpenApiDocument()
    {
        await using var factory = new ApiFactory(environment: "Development");
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.StartsWith("3.", body.RootElement.GetProperty("openapi").GetString());
        Assert.False(body.RootElement.GetProperty("paths").TryGetProperty("/api/jobs", out _));
    }

    [Theory]
    [InlineData("Production", "/openapi/v1.json")]
    [InlineData("Staging", "/openapi/v1.json")]
    [InlineData("Production", "/__tests/fault")]
    public async Task NonDevelopmentHostDoesNotExposeDocumentationOrTestEndpoints(string environment, string path)
    {
        await using var factory = new ApiFactory(environment);
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("application/json")]
    [InlineData("text/html")]
    public async Task ProductionExceptionsReturnSafeCorrelatedProblemDetails(string accept)
    {
        await using var factory = new ApiFactory(includeTestEndpoints: true);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Accept.ParseAdd(accept);
        using var response = await client.GetAsync("/__tests/fault");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var text = await response.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(text);
        var traceId = body.RootElement.GetProperty("traceId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(traceId));
        Assert.Equal(traceId, response.Headers.GetValues(RequestDiagnosticsMiddleware.TraceHeader).Single());
        Assert.Equal("An unexpected error occurred.", body.RootElement.GetProperty("title").GetString());
        Assert.Equal(500, body.RootElement.GetProperty("status").GetInt32());
        Assert.DoesNotContain(TestEndpointsController.ExceptionMarker, text);
        Assert.DoesNotContain("InvalidOperationException", text);
        Assert.DoesNotContain("stack", text, StringComparison.OrdinalIgnoreCase);

        var exception = Assert.Single(factory.Logs.Entries, entry => entry.EventId.Name == "UnhandledException");
        Assert.Equal(LogLevel.Error, exception.Level);
        Assert.Equal(traceId, exception.Properties["TraceId"]);
        Assert.Equal(TestEndpointsController.ExceptionMarker, exception.Exception?.Message);
        var completed = Assert.Single(factory.Logs.Entries, entry => entry.EventId.Name == "RequestCompleted");
        Assert.Equal(500, completed.Properties["StatusCode"]);
        Assert.Equal(traceId, completed.Properties["TraceId"]);
    }

    [Fact]
    public async Task CompletionLogsDoNotRecordRequestBodiesQueriesOrRawKeys()
    {
        await using var factory = new ApiFactory(includeTestEndpoints: true);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Idempotency-Key", "private-header-value");
        using var response = await client.PostAsync("/__tests/complete?secret=private-query-value",
            new StringContent("private-body-value", Encoding.UTF8, "text/plain"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var completed = Assert.Single(factory.Logs.Entries, entry => entry.EventId.Name == "RequestCompleted");
        Assert.Equal("POST", completed.Properties["Method"]);
        Assert.Equal("__tests/complete", completed.Properties["Route"]);
        Assert.Equal(204, completed.Properties["StatusCode"]);
        Assert.True(Assert.IsType<double>(completed.Properties["ElapsedMilliseconds"]) >= 0);
        var loggedProperties = JsonSerializer.Serialize(completed.Properties);
        Assert.DoesNotContain("private-", loggedProperties);
    }

    [Theory]
    [InlineData("JobPosting:BusinessTimeZone", "not/a-real-timezone")]
    [InlineData("JobPosting:BusinessTimeZone", "")]
    [InlineData("JobPosting:MaximumRequestBodyBytes", "0")]
    [InlineData("JobPosting:MaximumRequestBodyBytes", "1048577")]
    public void InvalidConfigurationFailsAtStartup(string key, string value)
    {
        using var factory = new ApiFactory(settings: new Dictionary<string, string?> { [key] = value });
        var exception = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());

        Assert.Contains(key, exception.Message);
    }

    [Fact]
    public async Task ConfigurationOverridesAndInjectedTimeProviderAreAvailableThroughDi()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero));
        await using var factory = new ApiFactory(settings: new Dictionary<string, string?>
        {
            ["JobPosting:BusinessTimeZone"] = "UTC",
            ["JobPosting:MaximumRequestBodyBytes"] = "32768"
        }, clock: clock);
        using var client = factory.CreateClient();

        Assert.Same(clock, factory.Services.GetRequiredService<TimeProvider>());
        Assert.Equal(clock.GetUtcNow(), factory.Services.GetRequiredService<TimeProvider>().GetUtcNow());
        Assert.Equal("UTC", factory.Services.GetRequiredService<TimeZoneInfo>().Id);
        Assert.Equal(32768, factory.Services.GetRequiredService<IOptions<JobPostingOptions>>().Value.MaximumRequestBodyBytes);
        Assert.Equal(32768, factory.Services.GetRequiredService<IOptions<KestrelServerOptions>>().Value.Limits.MaxRequestBodySize);
        Assert.NotNull(factory.Services.GetRequiredService<CreateJobRequestReader>());
        Assert.NotNull(factory.Services.GetRequiredService<JobRequestValidator>());
        Assert.NotNull(factory.Services.GetRequiredService<NewJobTemporalValidator>());
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
