using JobSearch.Api.Search;
using JobSearch.Api.Contracts;
using JobSearch.Api.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Text.Json;
namespace JobSearch.Api.Tests;

[Trait("Category", "Host")]
public sealed class SearchHttpTests
{
    [Fact]
    public async Task HttpListDetailAndSchemasMatchReadContract()
    {
        var store = new SearchReadTests.Store { Rows = [SearchReadTests.Row(1)], Detail = new(SearchReadTests.Row(1).Id, SearchReadTests.Now.AddTicks(12), "title", "department", "location", "full description", 1, 2, new(2026, 10, 4)) };
        await using var factory = new ApiFactory(environment: "Development", configureServices: s => s.AddSingleton<IJobReadStore>(store)); using var client = factory.CreateClient();
        using var list = JsonDocument.Parse(await client.GetStringAsync("/api/jobs?limit=1")); var item = list.RootElement.GetProperty("items")[0]; Assert.False(item.TryGetProperty("description", out _)); Assert.Equal("2026-10-05T23:59:59.999Z", item.GetProperty("createdAt").GetString()); Assert.Equal(JsonValueKind.Null, list.RootElement.GetProperty("nextCursor").ValueKind);
        using var detail = JsonDocument.Parse(await client.GetStringAsync("/api/jobs/" + store.Detail.Id)); Assert.Equal("full description", detail.RootElement.GetProperty("description").GetString()); Assert.Equal("2026-10-04", detail.RootElement.GetProperty("closingDate").GetString());
        using var schema = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json")); var paths = schema.RootElement.GetProperty("paths"); Assert.True(paths.GetProperty("/api/jobs").TryGetProperty("get", out _)); Assert.False(paths.GetProperty("/api/jobs").TryGetProperty("post", out _)); var responses = paths.GetProperty("/api/jobs/{id}").GetProperty("get").GetProperty("responses"); Assert.True(responses.TryGetProperty("404", out _)); Assert.True(responses.TryGetProperty("503", out _));
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PostAsync("/api/jobs", null)).StatusCode);
    }
    [Theory]
    [InlineData("/api/jobs?q=a&q=b", "invalid_query")]
    [InlineData("/api/jobs?unknown=x", "invalid_query")]
    [InlineData("/api/jobs?limit=51", "invalid_query")]
    [InlineData("/api/jobs?cursor=tampered", "invalid_cursor")]
    [InlineData("/api/jobs/not-a-uuid", "invalid_id")]
    public async Task HttpBadRequestsReturnSafeProblems(string path, string code)
    {
        await using var factory = new ApiFactory(); using var client = factory.CreateClient(); using var response = await client.GetAsync(path); Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType); using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); Assert.Equal(code, problem.RootElement.GetProperty("code").GetString()); Assert.False(string.IsNullOrWhiteSpace(problem.RootElement.GetProperty("traceId").GetString()));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DatabaseErrorsAre503AndUnexpectedErrorsAreSafe500(bool unexpected)
    {
        var store = new SearchReadTests.Store { Error = unexpected ? new InvalidOperationException("private-query-secret") : new Npgsql.NpgsqlException("private-query-secret") };
        await using var factory = new ApiFactory(configureServices: s => s.AddSingleton<IJobReadStore>(store)); using var client = factory.CreateClient();
        foreach (var path in new[] { "/api/jobs", "/api/jobs/" + Guid.NewGuid() }) { using var response = await client.GetAsync(path); Assert.Equal(unexpected ? HttpStatusCode.InternalServerError : HttpStatusCode.ServiceUnavailable, response.StatusCode); Assert.DoesNotContain("private-query-secret", await response.Content.ReadAsStringAsync()); }
        Assert.All(factory.Logs.Entries, e => { Assert.Null(e.Exception); Assert.DoesNotContain("private-query-secret", string.Join(',', e.Properties.Values)); });
    }
    [Fact]
    public async Task AuthenticRetiredCursorIs409AndMissingDetailIs404()
    {
        await using var factory = new ApiFactory(clock: new Clock(), configureServices: s => s.AddSingleton<IJobReadStore>(new SearchReadTests.Store())); using var client = factory.CreateClient(); var codec = factory.Services.GetRequiredService<CursorCodec>(); var query = new SearchQuery(null, null, null, 1, "newest", null);
        var payload = codec.Start(query, SearchReadTests.Now, 42) with { Version = 0, Last = SearchReadTests.Row(1).Position() };
        using var response = await client.GetAsync("/api/jobs?limit=1&cursor=" + codec.Encode(payload)); Assert.Equal(HttpStatusCode.Conflict, response.StatusCode); using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); Assert.Equal("cursor_expired", problem.RootElement.GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/jobs/" + Guid.NewGuid())).StatusCode);
    }
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => SearchReadTests.Now; }
    [Fact]
    public void ProductionRejectsUnconfiguredOrKnownDevelopmentKey()
    {
        foreach (var secret in new[] { "", CursorOptions.DevelopmentKey }) { using var factory = new ApiFactory(settings: new() { { "Cursor:Keys:current", secret } }); Assert.Throws<Microsoft.Extensions.Options.OptionsValidationException>(() => factory.CreateClient()); }
    }
}
