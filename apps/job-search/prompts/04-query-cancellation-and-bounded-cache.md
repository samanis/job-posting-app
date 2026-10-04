# Stage 4: Read coordination and bounded cache

Read apps/job-search/prompts/shared-requirements.md. Stages 1-3 must be complete. Implement only this stage.

1. Connect committed URL query state to one owned read pipeline. Use switchMap (or an equivalent proven cancellation design) so rapid changes unsubscribe old HttpClient requests. Stale completions must never replace newer results. Dispose requests/timers on destruction.
2. Define initial-loading, ready, empty, refreshing, stale-error, error, not-ready and throttled states with clear query identity. Results from another query must not appear as current matches. Same-query cached data may remain visible during refresh only with a freshness indicator.
3. Add a bounded injected-clock memory cache with shared limit 50 entries, 30-second TTL defaults and LRU eviction. Keys must include endpoint/API scope and every effective parameter. Cache only validated successes; enforce page-size bounds before storage. Do not persist job data in localStorage/sessionStorage.
4. Define ownership of in-flight requests and deduplicate simultaneous identical reads. Release entries on success, failure and cancellation; cancel underlying work when no consumers remain. Avoid leaked shareReplay subscriptions and caching canceled streams. Explicit refresh bypasses stored data and has a documented in-flight policy.
5. TTL expiration triggers reads only through user navigation/query/refresh, not background polling. Do not automatically retry; explicit retry observes valid throttle deadline using injected time. Query/detail cache consistency and detail-unavailable invalidation must be explicit.
6. Handle expired cursor by explaining the issue and offering first-page reload, never silently restarting a navigation loop. Retry preserves committed criteria.

Acceptance: tests prove latest-query wins, actual request cancellation, identical-read deduplication, no duplicate URL dispatch, exact TTL boundary, LRU eviction, cache-key isolation, refresh bypass, uncached errors and teardown. Use controlled timers, not real waits. Document that server indexing/read models/CDN/load tests are future backend concerns, not accomplished by the client cache.
