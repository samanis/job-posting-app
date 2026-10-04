# Read coordination and bounded caching

Stage 4 implements client read coordination, not server scale. Stage 5 renders summary cards and full details; see list-and-detail-experience.md. No backend is implemented.

## Ownership and cancellation

The list page transforms route query state into one page-scoped workflow subscription. Normalized duplicate criteria are suppressed before dispatch. URL criteria and explicit refresh commands share one switchMap; changing query cancels the previous consumer. An older response cannot replace the active query's state. Destroying the page releases its subscription and input debounce timer.

Root QueryReads owns a memory-only in-flight registry. Identical list/detail requests share one ReplaySubject-backed RxJS share stream. The last unsubscribe aborts the underlying HttpClient request. Completion, failure and cancellation release the registry entry. Stored/canceled streams are not retained for future reads. A subscriber that starts a new request during response delivery cannot have its registry entry deleted by the older stream's finalizer.

Explicit refresh bypasses stored data but joins an existing read for the same key. While the page already has an active request, duplicate refresh commands are ignored. There are no polling, background TTL timers, automatic retries, speculative detail prefetch or per-card detail fetches.

## Cache policy

List/detail successes share one LRU, default maximum 50 entries and 30-second TTL. Configurable QUERY_CACHE_ENTRIES clamps to integer 1-50 (nonfinite values use 50); QUERY_CACHE_TTL_MS clamps to 0-300000 ms (nonfinite values use 30000). QUERY_CACHE_SCOPE defaults to /api/jobs and distinguishes API/read-model scope; future changes to origin or tenant context must provide an appropriate scope and recreate the injector/cache. There is no auth/tenant implementation.

Keys contain scope, resource kind, normalized q/department/location, sort, limit and opaque cursor, or the exact detail identifier. Query text is trimmed without lowercasing. TTL starts at successful delivery; fresh means now strictly less than expires, so the exact boundary is expired. Cache access updates LRU order. Expired entries remain bounded and may supply stale data; visiting/refreshing triggers revalidation. Expiry alone dispatches nothing.

Only validated, frozen transport successes enter the cache. Page count cannot exceed requested limit (maximum 50); summary snapshots omit descriptions. Failures and canceled reads never populate it. A failed refresh preserves the prior successful snapshot and its original expiry rather than extending freshness. Data never goes to localStorage/sessionStorage.

Detail unavailable (404/410) removes its cached detail and cached pages containing that id. Ordinary detail failures retain prior successful data. Summary reads do not construct or overwrite full details. Independent list/detail TTLs can yield temporary differences; refreshing a detail does not silently alter unrelated query membership. Already-rendered pages and concurrent successful reads are not a globally coherent server snapshot; later explicit refresh and backend cursor/read-model guarantees remain necessary.

## List states and recovery

Initial-loading clears other-query data. Ready/empty contain the exact active query identity. Same-key refresh can show cached data with an explicit refreshing label. Refresh failure with cache produces stale-error and a visible freshness warning; uncached failure produces error/not-ready/throttled. No failure is presented as an empty match set.

Retry/Refresh preserves committed criteria and bypasses stored data. A usable Retry-After deadline blocks dispatch until the injected clock reaches it; no countdown interval or automatic retry is scheduled. The transport's documented delay policy applies. Expired cursors show an explanation and a first-page button, never an automatic restart loop. Results pagination derives nextCursor from the active workflow page.

## Verification and limits

Unit/HTTP tests exercise stale cancellation, duplicate URL emissions, simultaneous consumers, final-subscriber cancellation, reentrant completion, exact TTL, cache-key isolation, shared-capacity LRU eviction, invalid-page rejection, detail invalidation, refresh failure, explicit throttled retries and teardown. Router integration tests verify one initial request and one settled-input request, with no duplicate request for equivalent text.

These checks measure client behavior against controlled responses. Server indexing, keyset plans, read models, propagation, CDN/HTTP caching, overload handling and real latency/throughput need future backend/integration/load tests. No backend scalability guarantee or end-to-end posting/search integration has been claimed.

## Stage 7 browser measurements (2026-10-04)

Reproduce with npm.cmd run test:e2e from apps/job-search. query-performance.spec.ts records raw Chromium timeline JSON and Performance metrics for desktop/mobile runs under ignored test-results, attached to the HTML report. This captures a development Angular page, intercepted responses and browser/test overhead, not production-user timing or API latency.

The conceptual dataset has 100000 jobs, but fixtures generate only requested pages. Navigating initial page and Next rendered 20 cards at each step, made exactly 2 list GETs and 0 detail GETs. The two summary-only fixture payloads were 3883 and 3889 bytes. No global dataset is materialized or transmitted. Separate flows proved one request for settled rapid typing, no duplicate after Enter, cached Back/Forward without extra reads, and explicit refresh discovering a changed fixture. These are client request-count observations, not load tests.

| Captured metric | Desktop Chromium | Pixel 7 Chromium |
| --- | --- | --- |
| Layout count | 6 | 7 |
| Style recalculation count | 11 | 11 |
| Layout duration | 20.31 ms | 23.47 ms |
| Script duration | 69.24 ms | 67.63 ms |
| Task duration | 336.88 ms | 338.87 ms |
| Timeline events | 925 | 942 |

The representative trace's largest inspected FunctionCall was 21.35 ms desktop/25.47 ms mobile, associated with bundled development Angular code; the largest inspected Layout was 8.48/8.91 ms. This limited trace did not expose dataset-sized DOM growth or per-card requests. It cannot establish a universal latency threshold or identify production bottlenecks without a representative deployment/API/device workload.

Production build command npm.cmd run build passed: initial raw bundle 282.33 kB, estimated transfer 79.45 kB; lazy detail chunk 5.80 kB raw, not-found 447 bytes. Budgets remain initial warning 500 kB/error 1 MB, component style warning 4 kB/error 8 kB. Estimated transfer is Angular's estimate, not measured network delivery. Next validation should use real indexed APIs and production hosting plus representative client/server load.

References: [RxJS switchMap](https://rxjs.dev/api/operators/switchMap), [RxJS share](https://rxjs.dev/api/operators/share), and the installed RxJS 7.8 implementation/type declarations.
