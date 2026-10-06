using System.Net;
using JobSearch.Api.Contracts;
using JobSearch.Api.Search;
using JobSearch.Api.Tests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace JobSearch.Api.Tests;

[Trait("Category", "Unit")]
public sealed class SearchCachingTests
{
    [Fact]
    public void RegistrationProvidesOutputCacheWithBothPolicies()
    {
        var services = new ServiceCollection();
        services.AddSearchCaching();
        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IOptions<OutputCacheOptions>>().Value);
        Assert.NotNull(provider.GetRequiredService<IOutputCacheStore>());
    }

    [Fact]
    public async Task SuccessfulResponsesCarryCacheHeadersAndErrorsAreNeverStored()
    {
        var store = new SearchReadTests.Store { Rows = [SearchReadTests.Row(2), SearchReadTests.Row(1)] };
        var controller = Controller(store);

        var page = Assert.IsType<JobPage>(Assert.IsType<OkObjectResult>(await controller.List(default)).Value);
        Assert.Equal(SearchCaching.FirstPageHeader, CacheControl(controller));

        controller.ControllerContext.HttpContext = Context(new() { ["limit"] = "1", ["cursor"] = page.NextCursor });
        Assert.IsType<OkObjectResult>(await controller.List(default));
        Assert.Equal(SearchCaching.ContinuationPageHeader, CacheControl(controller));

        controller.ControllerContext.HttpContext = Context(new() { ["unknown"] = "x" });
        Assert.Equal(400, Assert.IsType<ObjectResult>(await controller.List(default)).StatusCode);
        Assert.Equal(SearchCaching.NoStoreHeader, CacheControl(controller));

        var id = SearchReadTests.Row(1).Id.ToString("D");
        controller.ControllerContext.HttpContext = Context(new());
        Assert.Equal(404, Assert.IsType<ObjectResult>(await controller.Detail(id, default)).StatusCode);
        Assert.Equal(SearchCaching.NoStoreHeader, CacheControl(controller));

        store.Detail = new(SearchReadTests.Row(1).Id, SearchReadTests.Now, "t", "d", "l", "description", 1, 2, new(2026, 10, 4));
        controller.ControllerContext.HttpContext = Context(new());
        Assert.IsType<OkObjectResult>(await controller.Detail(id, default));
        Assert.Equal(SearchCaching.DetailHeader, CacheControl(controller));

        store.Error = new Npgsql.NpgsqlException("unavailable");
        controller.ControllerContext.HttpContext = Context(new());
        Assert.Equal(503, Assert.IsType<ObjectResult>(await controller.Detail(id, default)).StatusCode);
        Assert.Equal(SearchCaching.NoStoreHeader, CacheControl(controller));
    }

    private static JobsController Controller(SearchReadTests.Store store) =>
        new(new(TimeProvider.System), new CursorCodec(Options.Create(SearchReadTests.Settings())), store, TimeProvider.System, NullLogger<JobsController>.Instance)
        {
            ControllerContext = new() { HttpContext = Context(new() { ["limit"] = "1" }) }
        };

    private static DefaultHttpContext Context(Dictionary<string, StringValues> query) =>
        new() { Request = { Query = new QueryCollection(query) } };

    private static string CacheControl(ControllerBase controller) => controller.Response.Headers.CacheControl.ToString();
}

[Trait("Category", "Host")]
public sealed class SearchCachingHttpTests
{
    [Fact]
    public async Task RepeatedListRequestIsServedFromCacheAndEachQueryIsCachedSeparately()
    {
        var store = new CountingStore { Rows = [SearchReadTests.Row(2), SearchReadTests.Row(1)] };
        await using var factory = Factory(store);
        using var client = factory.CreateClient();

        using var first = await client.GetAsync("/api/jobs?limit=1");
        var body = await first.Content.ReadAsStringAsync();
        using var second = await client.GetAsync("/api/jobs?limit=1");

        Assert.Equal(1, store.ListCalls);
        Assert.Equal(body, await second.Content.ReadAsStringAsync());
        Assert.Equal(SearchCaching.FirstPageHeader, second.Headers.CacheControl!.ToString());

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/jobs?limit=1&location=Toronto")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/jobs?limit=1&sort=closing-soon")).StatusCode);
        Assert.Equal(3, store.ListCalls);
    }

    [Fact]
    public async Task CursorPagesAreCachedPerExactCursorValue()
    {
        var store = new CountingStore { Rows = [SearchReadTests.Row(2), SearchReadTests.Row(1)] };
        await using var factory = Factory(store);
        using var client = factory.CreateClient();
        using var firstPage = System.Text.Json.JsonDocument.Parse(await client.GetStringAsync("/api/jobs?limit=1"));
        var cursor = firstPage.RootElement.GetProperty("nextCursor").GetString()!;

        using var page = await client.GetAsync("/api/jobs?limit=1&cursor=" + cursor);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Equal(SearchCaching.ContinuationPageHeader, page.Headers.CacheControl!.ToString());

        // Cursors are case-sensitive; a case-altered cursor must be verified, not served the cached page.
        var index = cursor.IndexOfAny("abcdefghijklmnopqrstuvwxyz".ToCharArray());
        var altered = cursor[..index] + char.ToUpperInvariant(cursor[index]) + cursor[(index + 1)..];
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/jobs?limit=1&cursor=" + altered)).StatusCode);
    }

    [Fact]
    public async Task DetailIsCachedButMissingJobsAndFailuresAreNot()
    {
        var store = new CountingStore();
        await using var factory = Factory(store);
        using var client = factory.CreateClient();
        var id = SearchReadTests.Row(1).Id.ToString("D");

        using var missing = await client.GetAsync("/api/jobs/" + id);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.True(missing.Headers.CacheControl!.NoStore);

        // The job is ingested moments later and must become visible immediately.
        store.Detail = new(SearchReadTests.Row(1).Id, SearchReadTests.Now, "t", "d", "l", "description", 1, 2, new(2026, 10, 4));
        using var found = await client.GetAsync("/api/jobs/" + id);
        Assert.Equal(HttpStatusCode.OK, found.StatusCode);
        Assert.Equal(SearchCaching.DetailHeader, found.Headers.CacheControl!.ToString());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/jobs/" + id)).StatusCode);
        Assert.Equal(2, store.DetailCalls);

        var other = SearchReadTests.Row(2).Id.ToString("D");
        store.Error = new Npgsql.NpgsqlException("unavailable");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/api/jobs/" + other)).StatusCode);
        store.Error = null;
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/jobs/" + other)).StatusCode);
    }

    [Fact]
    public async Task HealthEndpointsAreNotCached()
    {
        await using var factory = Factory(new CountingStore());
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/health/live");
        Assert.Null(response.Headers.CacheControl);
    }

    private static ApiFactory Factory(CountingStore store) =>
        new(configureServices: services => services.AddSingleton<IJobReadStore>(store));

    private sealed class CountingStore : IJobReadStore
    {
        public List<ReadRow> Rows = [];
        public JobDetail? Detail;
        public Exception? Error;
        public int ListCalls;
        public int DetailCalls;

        public Task<long> WatermarkAsync(CancellationToken token) => Task.FromResult(42L);

        public Task<List<ReadRow>> ListAsync(SearchQuery query, CursorPayload snapshot, bool continuation, CancellationToken token)
        {
            Interlocked.Increment(ref ListCalls);
            return Task.FromResult(Rows);
        }

        public Task<JobDetail?> DetailAsync(Guid id, CancellationToken token)
        {
            Interlocked.Increment(ref DetailCalls);
            return Error is null ? Task.FromResult(Detail) : Task.FromException<JobDetail?>(Error);
        }
    }
}
