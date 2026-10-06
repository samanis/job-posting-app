using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using JobPosting.Api.Contracts;
using JobPosting.Api.Messaging;
using JobPosting.Api.Persistence;
using JobPosting.Api.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace JobPosting.Api.Tests;

[Trait("Category", "Host")]
public sealed class PostingEndpointTests
{
    private static ApiFactory Host(PostingWorkflowTests.Harness harness) => new(clock: harness.Clock, configureServices: services =>
    { services.AddSingleton<IPostingWriteStore>(harness); services.AddSingleton<IJobEventPublisher>(harness); });
    private static HttpRequestMessage Request(string key = "key")
    { var request = new HttpRequestMessage(HttpMethod.Post, "/api/jobs") { Content = JsonContent.Create(PostingWorkflowTests.Request()) }; request.Headers.Add("Idempotency-Key", key); return request; }
    [Fact]
    public async Task HttpSuccessReturnsFullStableSnapshotAndReplayDoesNotRepublish()
    {
        using var harness = new PostingWorkflowTests.Harness(); await using var factory = Host(harness); using var client = factory.CreateClient();
        using var request = Request(); using var first = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode); var body = await first.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body); Assert.Equal("accepted", json.RootElement.GetProperty("status").GetString());
        Assert.Equal("Engineer", json.RootElement.GetProperty("title").GetString()); Assert.Null(first.Headers.Location);
        using var retry = Request(); using var second = await client.SendAsync(retry);
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode); Assert.Equal(body, await second.Content.ReadAsStringAsync()); Assert.Equal(1, harness.Calls.Count(value => value == "publish"));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HttpPublicationAndCleanupFailuresAreSafe503(bool cleanupFails)
    {
        using var harness = new PostingWorkflowTests.Harness(); harness.Publish = (_, _) => Task.FromException(new IOException("private-host secret-payload"));
        if (cleanupFails) harness.Delete = _ => Task.FromException<bool>(new IOException("private-cleanup"));
        await using var factory = Host(harness); using var client = factory.CreateClient(); using var request = Request(); using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode); var body = await response.Content.ReadAsStringAsync(); Assert.DoesNotContain("private", body);
        using var json = JsonDocument.Parse(body); Assert.Equal(cleanupFails ? JobApiProblems.PublicationUnresolved : JobApiProblems.PublicationFailed, json.RootElement.GetProperty("code").GetString());
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        Assert.NotNull(response.Headers.RetryAfter); Assert.True(json.RootElement.TryGetProperty("traceId", out _));
    }
    [Theory]
    [InlineData("{", 400)]
    [InlineData("{\"unknown\":1}", 400)]
    [InlineData("{\"salaryMin\":10.001}", 422)]
    public async Task StrictJsonErrorsDoNotWriteOrPublish(string json, int status)
    {
        using var harness = new PostingWorkflowTests.Harness(); await using var factory = Host(harness); using var client = factory.CreateClient();
        using var content = new StringContent(json, Encoding.UTF8, "application/json"); using var response = await client.PostAsync("/api/jobs", content);
        Assert.Equal(status, (int)response.StatusCode); Assert.Empty(harness.Calls);
    }
    [Fact]
    public async Task OversizedAndUnsupportedContentAreRejectedBeforePersistence()
    {
        using var harness = new PostingWorkflowTests.Harness(); await using var factory = Host(harness); using var client = factory.CreateClient();
        using var large = new StringContent(new string('x', 65537), Encoding.UTF8, "application/json"); using var response = await client.PostAsync("/api/jobs", large); Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        using var text = new StringContent("{}", Encoding.UTF8, "text/plain"); using var unsupported = await client.PostAsync("/api/jobs", text); Assert.Equal(HttpStatusCode.UnsupportedMediaType, unsupported.StatusCode); Assert.Empty(harness.Calls);
    }
}
