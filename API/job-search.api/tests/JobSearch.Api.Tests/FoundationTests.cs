using System.Net;
using System.Text;
using System.Text.Json;
using JobSearch.Api.Configuration;
using JobSearch.Api.Diagnostics;
using JobSearch.Api.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
namespace JobSearch.Api.Tests;
[Trait("Category", "Host")]
public sealed class FoundationTests
{
    [Fact]
    public async Task DevelopmentExposesARealOpenApiDocument()
    {
        await using var factory = new ApiFactory(environment: "Development");
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.StartsWith("3.", body.RootElement.GetProperty("openapi").GetString());
        Assert.Equal(5,body.RootElement.GetProperty("paths").EnumerateObject().Count());
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
        Assert.Null(exception.Exception);
        Assert.Equal("InvalidOperationException", exception.Properties["Failure"]);
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
    [InlineData("0")]
    [InlineData("1048577")]
    public void InvalidConfigurationFailsStartup(string value)
    {
        using var factory = new ApiFactory(settings: new() { ["Search:MaximumEventBodyBytes"] = value });
        var error = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
        Assert.Contains("Search:MaximumEventBodyBytes", error.Message);
    }
    [Fact]
    public async Task FoundationHasNoSearchOrDependencyEndpointsAndClockIsInjectable()
    {
        var clock = new FixedClock();
        await using var factory = new ApiFactory(clock: clock, settings: new() { ["Search:MaximumEventBodyBytes"] = "32768" });
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/jobs/not-a-uuid")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
        Assert.Same(clock, factory.Services.GetRequiredService<TimeProvider>());
        Assert.Equal(32768, factory.Services.GetRequiredService<IOptions<SearchOptions>>().Value.MaximumEventBodyBytes);
        Assert.Equal(TimeSpan.FromSeconds(30), factory.Services.GetRequiredService<IOptions<Microsoft.Extensions.Hosting.HostOptions>>().Value.ShutdownTimeout);
    }
    [Theory]
    [InlineData("SearchDatabase:ConnectionString", "private-secret=invalid")]
    [InlineData("SearchDatabase:CommandTimeoutSeconds", "0")]
    public void InvalidDatabaseConfigurationFailsStartupSafely(string key,string value)
    {
        using var factory=new ApiFactory(settings:new(){[key]=value});
        var error=Assert.Throws<OptionsValidationException>(()=>factory.CreateClient());
        Assert.Contains(key,error.Message);Assert.DoesNotContain("private-secret",error.Message);
    }
    [Theory]
    [InlineData("RabbitMq:HostName", " ")]
    [InlineData("RabbitMq:Prefetch", "0")]
    [InlineData("RabbitMq:Concurrency", "11")]
    public void InvalidConsumerConfigurationFailsStartup(string key,string value)
    {
        using var factory=new ApiFactory(settings:new(){[key]=value});
        var error=Assert.Throws<OptionsValidationException>(()=>factory.CreateClient());Assert.Contains(key,error.Message);
    }
    private sealed class FixedClock : TimeProvider { }
}
