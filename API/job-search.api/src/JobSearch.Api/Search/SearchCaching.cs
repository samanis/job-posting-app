using Microsoft.AspNetCore.OutputCaching;

namespace JobSearch.Api.Search;

// Immutable details and short-lived first pages are cached. Continuations always
// validate their cursor; first pages expire at the UTC availability boundary.
public static class SearchCaching
{
    public const string ListPolicy = "job-list";
    public const string DetailPolicy = "job-detail";

    // Server-side output cache lifetimes.
    public static readonly TimeSpan ListLifetime = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan DetailLifetime = TimeSpan.FromHours(1);

    // Cache-Control for browsers, proxies and CDNs.
    public const string FirstPageHeader = "public, max-age=15";
    public const string ContinuationPageHeader = "no-store";
    public const string DetailHeader = "public, max-age=86400, immutable";
    public const string NoStoreHeader = "no-store";

    public static IServiceCollection AddSearchCaching(this IServiceCollection services) =>
        services.AddOutputCache(options =>
        {
            // "*" keys entries by every query parameter, so each filter/sort/cursor combination is separate.
            options.AddPolicy(ListPolicy, policy => policy.Expire(ListLifetime).SetVaryByQuery("*").AddPolicy<ListFreshnessPolicy>());
            options.AddPolicy(DetailPolicy, policy => policy.Expire(DetailLifetime).AddPolicy<CacheObservationPolicy>());
        });

    public static TimeSpan FirstPageLifetime(DateTimeOffset now) =>
        TimeSpan.FromSeconds(Math.Min(ListLifetime.TotalSeconds,
            Math.Floor((now.UtcDateTime.Date.AddDays(1) - now.UtcDateTime).TotalSeconds)));

    public static string FirstPageCacheControl(DateTimeOffset now) =>
        $"public, max-age={(int)FirstPageLifetime(now).TotalSeconds}";
}

// Continuations must execute cursor validation on every request. First pages cannot
// cross the UTC availability boundary, even when an older entry remains in memory.
public sealed class ListFreshnessPolicy(TimeProvider clock) : IOutputCachePolicy
{
    public ValueTask CacheRequestAsync(OutputCacheContext context, CancellationToken token)
    {
        var now = clock.GetUtcNow();
        context.CacheVaryByRules.VaryByValues["utc-day"] = now.UtcDateTime.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var lifetime = SearchCaching.FirstPageLifetime(now);
        context.ResponseExpirationTimeSpan = lifetime;
        context.HttpContext.Response.OnStarting(() =>
        {
            var responseNow = clock.GetUtcNow();
            if (!context.HttpContext.Request.Query.ContainsKey("cursor") && context.HttpContext.Response.StatusCode == 200)
                context.HttpContext.Response.Headers.CacheControl = responseNow.UtcDateTime.Date == now.UtcDateTime.Date
                    ? SearchCaching.FirstPageCacheControl(responseNow) : SearchCaching.NoStoreHeader;
            return Task.CompletedTask;
        });
        if (context.HttpContext.Request.Query.ContainsKey("cursor") || lifetime <= TimeSpan.Zero)
        {
            context.AllowCacheLookup = false;
            context.AllowCacheStorage = false;
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask ServeFromCacheAsync(OutputCacheContext context, CancellationToken token)
    {
        JobSearch.Api.Diagnostics.SearchMetrics.CacheHit("list");
        return ValueTask.CompletedTask;
    }
    public ValueTask ServeResponseAsync(OutputCacheContext context, CancellationToken token)
    {
        // Bound server storage from the time the response actually finishes.
        if (!context.HttpContext.Request.Query.ContainsKey("cursor") && context.HttpContext.Response.StatusCode == 200)
        {
            var now = clock.GetUtcNow();
            var lifetime = SearchCaching.FirstPageLifetime(now);
            context.ResponseExpirationTimeSpan = lifetime;
            if (lifetime <= TimeSpan.Zero || context.CacheVaryByRules.VaryByValues["utc-day"] != now.UtcDateTime.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture))
                context.AllowCacheStorage = false;
        }
        return ValueTask.CompletedTask;
    }
}

public sealed class CacheObservationPolicy : IOutputCachePolicy
{
    public ValueTask CacheRequestAsync(OutputCacheContext context, CancellationToken token) => ValueTask.CompletedTask;
    public ValueTask ServeResponseAsync(OutputCacheContext context, CancellationToken token) => ValueTask.CompletedTask;
    public ValueTask ServeFromCacheAsync(OutputCacheContext context, CancellationToken token)
    {
        JobSearch.Api.Diagnostics.SearchMetrics.CacheHit("detail");
        return ValueTask.CompletedTask;
    }
}
