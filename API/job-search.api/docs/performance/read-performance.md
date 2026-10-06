# Stage 7 local read workload

Run from API/job-search.api with Docker Linux containers and the pinned .NET SDK:

```powershell
dotnet restore JobSearch.slnx --locked-mode
dotnet run --project tools/ReadWorkload --configuration Release -- 10000 4 10
```

## Cache comparison (2026-10-06)

The list and detail endpoints use ASP.NET Core output caching (in memory; first page at most 15 s and capped at UTC midnight, detail 1 h; only 200 responses). Continuations bypass caching and send no-store so expiry is checked every time. See [SearchCaching.cs](../../src/JobSearch.Api/Search/SearchCaching.cs). The workload host registers the same policies; cached mode enables the middleware and uncached mode bypasses it.

The runner accepts a fourth argument, cached (default) or uncached. It records
separate cache-cached.json and cache-uncached.json reports; the historical latest.json
is preserved. Each mode owns and removes its own PostgreSQL fixture.

```powershell
dotnet run --project tools/ReadWorkload --configuration Release -- 10000 4 30 uncached
dotnet run --project tools/ReadWorkload --configuration Release -- 10000 4 30 cached
```

Both runs used 10,000 jobs, four concurrent clients and 30 seconds. Traffic includes
80% requests across six popular search/detail routes, 10% numbered substring searches,
and 10% rotating detail IDs. All groups eventually repeat; workers overlap their keys.
Popular routes are warmed before timing. This is an explicit synthetic workload,
not production traffic or a claim about its hit rate. No continuation traffic or
concurrent ingestion is included. The fixed clock avoids midnight during the benchmark;
expiry and midnight correctness are verified separately in HTTP tests.

| Measurement | Uncached | Local output cache |
| --- | ---: | ---: |
| Requests | 14,100 | 232,915 |
| Cache hit rate | 0% | 99.09% |
| Actual database reader commands | 24,913 | 4,120 |
| Database reads per request | 1.767 | 0.0177 |
| p95 HTTP latency | 20.33 ms | 1.12 ms |
| Requests per second | 469.70 | 7,760.74 |
| Errors | 0 | 0 |

Database reader commands are counted by an EF command interceptor on the HTTP host;
fixture setup, plans, and warmup are excluded. Cache hits are counted from output-cache
policy hit callbacks. Latencies include full response-body reads. The runs are sequential
with different owned databases and are closed-loop, so faster responses produce more
requests and more repeated keys. These measurements demonstrate cache benefits on
repeated reads, not a controlled production speedup or capacity guarantee.

Decision: keep the local output cache. There is no supplied production replica count,
cache memory pressure, or cross-replica miss evidence to justify Redis. Reconsider a
shared Redis output cache when multiple replicas materially duplicate work or cache
capacity becomes a measured limitation. Consider a Redis search projection only if
representative cache-miss query plans and latency show PostgreSQL search is the bottleneck.

Redis acceptance testing must include cache-outage behavior, shared-key versioning,
and the same cursor/midnight correctness checks. Redis deployment is deferred.

Arguments are seed rows1000..100000, concurrency1..32, duration1..60seconds. Defaults10000/4/10. There is no user connection string or external target argument. The tool creates a GUID-named owned PostgreSQL container, a new owned database, applies existing migrations and removes the container/volumes on disposal, including setup failures. Docker operations have2-minute bounds. Database commands and HTTP calls are bounded; new requests stop at duration and in-flight calls can drain up to the5-second HTTP timeout. A hard process kill can bypass disposal; inspect only the named jobsearch-workload-* fixture container if cleaning up manually. Never apply this seeder to a user database.

The tool bulk-seeds deterministic IDs with four roles/departments/locations, varied source creation minutes/ties, salaries and closing dates (about80% available), long repeated descriptions, uncommon title nebula (1/97) and description rarequasar (1/101). Direct bulk fixture seeding is for read workload only; it does not verify event fingerprinting/ingestion. VACUUM (ANALYZE) jobs runs after seeding so GIN pending inserts/statistics reflect a maintained database. This does not change migration history or production settings.

EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) runs actual EF-generated parameterized first-page commands, not hand-interpolated filter SQL. Both orders, rare title/description substrings and broad Engineer filters are captured. After warming each route, Kestrel loopback traffic mixes newest, closing-soon, rare title, rare description, AND department/location filters and detail GETs. Actual controllers, read store, cursor codec, JSON serializer and request/error middleware are used. The tool host disables logging and has no consumer; it is not a benchmark of the full deployed Program, production logging, ingestion contention or a remote network. Pagination correctness/commit boundaries and real broker isolation are tested separately.

Each cache comparison report includes UTC capture, host/runtime/CPU count, pinned PostgreSQL image, arguments, elapsed time, request count, cache hits, actual database reader commands, p50/p95/p99, attempts per second, error classifications, bytes and complete query plans/buffer evidence. Percentiles include attempted requests (including errors); these runs had none. Reruns overwrite only the selected mode's report. latest.json preserves the earlier Stage 7 run below. Never put secrets or connection strings in reports.

## Actual final run

Captured 2026-10-05T23:36:47.0778188+00:00; 10000 rows; concurrency4; requested10 seconds, elapsed10.009. Windows build26200, .NET10.0.1,16 logical CPUs; owned Docker PostgreSQL18.6. Docker CPU/memory allocation was not separately measured.

4146 requests, 414.22 attempts/second, p508.19ms, p9521.32ms, p9925.64ms, zero errors. Total response bytes17725567, largest observed response4993 bytes. That observed size is not a worst-case API payload bound.

| Query | EXPLAIN execution ms | Plan nodes/indexes |
| --- | ---: | --- |
| newest:unfiltered | 0.052 | Limit, ix_jobs_newest |
| closing-soon:unfiltered | 0.055 | Limit, ix_jobs_closing |
| newest:nebula | 0.550 | Limit, Sort, Bitmap Heap Scan, BitmapOr, ix_jobs_title_trgm, ix_jobs_description_trgm |
| closing-soon:rarequasar | 1.326 | Limit, Sort, Bitmap Heap Scan, BitmapOr, ix_jobs_title_trgm, ix_jobs_description_trgm |
| newest:Engineer | 0.485 | Limit, ix_jobs_newest |

Unfiltered queries use their existing order indexes. Selective title/description searches use the existing trigram indexes combined by BitmapOr and a small sort. Broad Engineer filtering uses the order index plus filter. No new index or migration was justified by these maintained local plans; response caching was added later (see below), and no search engine was added. An earlier unmaintained bulk seed chose an order scan/sequence scan for rare filters (22.473/89.085ms); after explicit fixture maintenance, the existing GIN indexes were chosen. Other concurrent test activity and cache state differed between runs, so that comparison is diagnostic evidence, not a controlled causal performance claim.

These are short warmed local measurements, not an SLA, sustained-load test or production capacity claim. Dataset text is repetitive and its distribution is synthetic; short substrings can have poor selectivity; real distributions, cold caches, logging, network/TLS, concurrent ingestion, hardware and autovacuum may alter plans/results. Test representative production-like data before tuning. EXPLAIN adds measurement overhead. Later operations must monitor table statistics/GIN pending lists and preserve automatic maintenance, rather than force a planner choice or rebuild indexes on assumption.

## Output caching: earlier quick run

> Superseded by the [cache comparison](#cache-comparison-2026-10-06) above, which uses a mixed workload and measures cache hits and database reads. This earlier run repeated six fixed URLs and is kept for the record.

This run used the earlier policy, under which pages with a cursor were also cached.

Both runs on 2026-10-06, same machine and command (`10000 4 10`), back to back:

| | Before caching | After caching |
| --- | ---: | ---: |
| Requests in 10 s | 4,929 | 148,836 |
| Requests/second | 493 | 14,874 |
| p50 | 6.93 ms | 0.19 ms |
| p95 | 17.20 ms | 0.69 ms |
| p99 | 21.12 ms | 1.11 ms |
| Errors | 0 | 0 |

**Read this as a best case.** The workload cycles through six fixed URLs, so after the first request for each almost every request is a cache hit and never reaches PostgreSQL. It shows what caching does for popular pages, such as the unfiltered first page, which most visitors load. Long-tail searches with unique filters miss the cache and perform like the "before" column. The client ran in the same process with only 4 concurrent requests, so the cached throughput figure may reflect the load generator as much as the API; it is not a capacity claim.

## Read and operational review

Read queries use AsNoTracking and server-side WHERE/order/summary projection with LIMIT+1. Summary SELECT excludes description (description may still be evaluated for q filtering). List limit1..50 and bounded field/cursor sizes bound JSON output; detail descriptions are bounded by ingestion. First list page uses two SQL reads (watermark + bounded page), continuation one, detail one; there is no per-item database query, offset scan or count. Existing query SQL tests check parameterization/no description projection/no offset/count; real sort/commit-boundary tests remain separate.

New real integration evidence starts Kestrel with actual search controllers/DB/consumer, stops ONLY its fixture broker app, observes ingestion Backoff and health503 while list/detail/read-ready/live all stay200, then starts the broker and observes ingestion Running/200. A separate owned PG table lock verifies actual caller cancellation and command timeout, then successful reading after rollback. Existing graceful/forced-drain and quarantine tests remain in the28-case full integration suite.

The timeout experiment exposed Npgsql EF's InvalidOperationException wrapper around a transient Npgsql exception. Search now classifies that specific wrapper as503/database; unrelated unexpected errors remain safe500. Unit checks verify both cases without broad exception suppression. Logs never attach raw exceptions/queries/cursors/secrets; existing host safety and meter-listener low-cardinality tests pass. Metrics label only outcome/state/status class. No exporter is installed by this stage.

Manual quarantine diagnostics: inspect only the configured search-owned quarantine queue and safe failureCode/original message identity; message bytes belong there, never in logs. Quarantine can have duplicates after lost acceptance/ACK. Diagnose malformed/version/conflict/topology/permissions problems, then explicitly decide whether a valid event should be republished. No automatic replay, shared source queue purge or posting database investigation is implemented.

References: [PostgreSQL EXPLAIN](https://www.postgresql.org/docs/18/sql-explain.html), [VACUUM and pending GIN inserts](https://www.postgresql.org/docs/18/sql-vacuum.html), [Npgsql execution strategy](https://www.npgsql.org/efcore/api/Npgsql.EntityFrameworkCore.PostgreSQL.Storage.Internal.NpgsqlExecutionStrategy.html).
