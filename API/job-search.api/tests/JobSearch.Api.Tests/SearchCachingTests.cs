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
    [Theory]
    [InlineData(false, 200, 0)]
    [InlineData(false, 200, 1)]
    [InlineData(false, 200, 2)]
    [InlineData(false, 200, 3)]
    [InlineData(false, 503, 0)]
    [InlineData(true, 200, 0)]
    public async Task PolicyHonorsCursorAndAvailabilityBoundaries(bool continuation, int status, int advance)
    {
        var clock = new PolicyClock(new DateTimeOffset(2026, 10, 6, 23, 59, 58, TimeSpan.Zero));
        var response = new StartingResponse();
        var http = new DefaultHttpContext();
        http.Features.Set<Microsoft.AspNetCore.Http.Features.IHttpResponseFeature>(response);
        http.Response.StatusCode = status;
        if (continuation) http.Request.QueryString = new("?cursor=opaque");
        var context = new OutputCacheContext { HttpContext = http, AllowCacheLookup = true, AllowCacheStorage = true };
        var policy = new ListFreshnessPolicy(clock);
        await policy.CacheRequestAsync(context, default);
        Assert.Equal(!continuation, context.AllowCacheLookup);
        Assert.Equal("2026-10-06", context.CacheVaryByRules.VaryByValues["utc-day"]);
        clock.Now = clock.Now.AddSeconds(advance);
        await response.StartAsync();
        await policy.ServeResponseAsync(context, default);
        if (!continuation && status == 200)
        {
            Assert.Equal(advance >= 2 ? "no-store" : $"public, max-age={2 - advance}", http.Response.Headers.CacheControl.ToString());
            Assert.Equal(advance < 2, context.AllowCacheStorage);
        }
        else Assert.Empty(http.Response.Headers);
        await policy.ServeFromCacheAsync(context, default);
        var detail = new CacheObservationPolicy();
        await detail.CacheRequestAsync(context, default);
        await detail.ServeResponseAsync(context, default);
        await detail.ServeFromCacheAsync(context, default);
    }

    [Fact]
    public async Task LastFractionOfUtcDayIsNotCached()
    {
        var clock = new PolicyClock(new DateTimeOffset(2026, 10, 6, 23, 59, 59, TimeSpan.Zero).AddMilliseconds(500));
        var context = new OutputCacheContext { HttpContext = new DefaultHttpContext(), AllowCacheLookup = true, AllowCacheStorage = true };
        await new ListFreshnessPolicy(clock).CacheRequestAsync(context, default);
        Assert.False(context.AllowCacheLookup);
        Assert.False(context.AllowCacheStorage);
        Assert.Equal(TimeSpan.Zero, context.ResponseExpirationTimeSpan);
        await new ListFreshnessPolicy(clock).ServeResponseAsync(context, default);
        Assert.False(context.AllowCacheStorage);
    }

    private sealed class PolicyClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class StartingResponse : Microsoft.AspNetCore.Http.Features.HttpResponseFeature
    {
        private Func<object, Task>? callback;
        private object? callbackState;
        public override void OnStarting(Func<object, Task> action, object state) { callback = action; callbackState = state; }
        public Task StartAsync() => callback!(callbackState!);
    }

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
    public async Task CachedContinuationCannotOutliveCursor()
    {
        var clock = new MutableClock(new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero));
        var store = new CountingStore { Rows = [SearchReadTests.Row(2), SearchReadTests.Row(1)] };
        await using var factory = new ApiFactory(clock: clock, configureServices: services => services.AddSingleton<IJobReadStore>(store));
        using var client = factory.CreateClient();
        using var first = System.Text.Json.JsonDocument.Parse(await client.GetStringAsync("/api/jobs?limit=1"));
        var path = "/api/jobs?limit=1&cursor=" + first.RootElement.GetProperty("nextCursor").GetString();
        clock.Now = clock.Now.AddMinutes(15).AddSeconds(-1);
        using var valid = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        Assert.True(valid.Headers.CacheControl!.NoStore);
        clock.Now = clock.Now.AddSeconds(1);
        using var expired = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Conflict, expired.StatusCode);
        Assert.Equal(2, store.ListCalls);
    }

    [Fact]
    public async Task FirstPageIsRefreshedAtUtcMidnight()
    {
        var clock = new MutableClock(new DateTimeOffset(2026, 10, 6, 23, 59, 58, TimeSpan.Zero));
        var store = new CountingStore { Rows = [SearchReadTests.Row(1)] };
        await using var factory = new ApiFactory(clock: clock, configureServices: services => services.AddSingleton<IJobReadStore>(store));
        using var client = factory.CreateClient();
        using var first = await client.GetAsync("/api/jobs");
        Assert.Equal(TimeSpan.FromSeconds(2), first.Headers.CacheControl!.MaxAge);
        store.Rows = [];
        clock.Now = clock.Now.AddSeconds(2);
        using var second = await client.GetAsync("/api/jobs");
        Assert.Equal(2, store.ListCalls);
        using var body = System.Text.Json.JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        Assert.Empty(body.RootElement.GetProperty("items").EnumerateArray());
    }

    private sealed class MutableClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

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
    public async Task CursorPagesAlwaysValidateEachRequest()
    {
        var store = new CountingStore { Rows = [SearchReadTests.Row(2), SearchReadTests.Row(1)] };
        await using var factory = Factory(store);
        using var client = factory.CreateClient();
        using var firstPage = System.Text.Json.JsonDocument.Parse(await client.GetStringAsync("/api/jobs?limit=1"));
        var cursor = firstPage.RootElement.GetProperty("nextCursor").GetString()!;

        using var page = await client.GetAsync("/api/jobs?limit=1&cursor=" + cursor);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Equal(SearchCaching.ContinuationPageHeader, page.Headers.CacheControl!.ToString());
        using var repeated = await client.GetAsync("/api/jobs?limit=1&cursor=" + cursor);
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        Assert.Equal(3, store.ListCalls);

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
