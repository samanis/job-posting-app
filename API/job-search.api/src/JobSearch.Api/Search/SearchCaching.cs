using Microsoft.AspNetCore.OutputCaching;

namespace JobSearch.Api.Search;

// Ingested jobs never change and search may be eventually consistent, so responses are
// cached by time alone: a new job appears once the short list lifetime lapses, and no
// eviction (or cache shared between replicas) is needed. Only 200 responses are stored.
public static class SearchCaching
{
    public const string ListPolicy = "job-list";
    public const string DetailPolicy = "job-detail";

    // Server-side output cache lifetimes.
    public static readonly TimeSpan ListLifetime = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan DetailLifetime = TimeSpan.FromHours(1);

    // Cache-Control for browsers, proxies and CDNs. A cursor pins its snapshot, so a
    // continuation page never changes; the first page changes as new jobs arrive.
    public const string FirstPageHeader = "public, max-age=15";
    public const string ContinuationPageHeader = "public, max-age=60";
    public const string DetailHeader = "public, max-age=86400, immutable";
    public const string NoStoreHeader = "no-store";

    public static IServiceCollection AddSearchCaching(this IServiceCollection services) =>
        services.AddOutputCache(options =>
        {
            // "*" keys entries by every query parameter, so each filter/sort/cursor combination is separate.
            options.AddPolicy(ListPolicy, policy => policy.Expire(ListLifetime).SetVaryByQuery("*"));
            options.AddPolicy(DetailPolicy, policy => policy.Expire(DetailLifetime));
        });
}
