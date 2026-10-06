# Stage 7 local read workload

Run from API/job-search.api with Docker Linux containers and the pinned .NET SDK:

```powershell
dotnet restore JobSearch.slnx --locked-mode
dotnet run --project tools/ReadWorkload --configuration Release -- 10000 4 10
```

Arguments are seed rows1000..100000, concurrency1..32, duration1..60seconds. Defaults10000/4/10. There is no user connection string or external target argument. The tool creates a GUID-named owned PostgreSQL container, a new owned database, applies existing migrations and removes the container/volumes on disposal, including setup failures. Docker operations have2-minute bounds. Database commands and HTTP calls are bounded; new requests stop at duration and in-flight calls can drain up to the5-second HTTP timeout. A hard process kill can bypass disposal; inspect only the named jobsearch-workload-* fixture container if cleaning up manually. Never apply this seeder to a user database.

The tool bulk-seeds deterministic IDs with four roles/departments/locations, varied source creation minutes/ties, salaries and closing dates (about80% available), long repeated descriptions, uncommon title nebula (1/97) and description rarequasar (1/101). Direct bulk fixture seeding is for read workload only; it does not verify event fingerprinting/ingestion. VACUUM (ANALYZE) jobs runs after seeding so GIN pending inserts/statistics reflect a maintained database. This does not change migration history or production settings.

EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) runs actual EF-generated parameterized first-page commands, not hand-interpolated filter SQL. Both orders, rare title/description substrings and broad Engineer filters are captured. After warming each route, Kestrel loopback traffic mixes newest, closing-soon, rare title, rare description, AND department/location filters and detail GETs. Actual controllers, read store, cursor codec, JSON serializer and request/error middleware are used. The tool host disables logging and has no consumer; it is not a benchmark of the full deployed Program, production logging, ingestion contention or a remote network. Pagination correctness/commit boundaries and real broker isolation are tested separately.

latest.json is the full latest report: UTC capture, host/runtime/CPU count, pinned PostgreSQL image, arguments, elapsed time, request count, p50/p95/p99, attempts per second, error classifications, bytes and complete query plans/buffer evidence. Percentiles include attempted requests (including errors); these runs had none. Reruns overwrite this application-local report. Never put secrets or connection strings in reports.

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

Unfiltered queries use their existing order indexes. Selective title/description searches use the existing trigram indexes combined by BitmapOr and a small sort. Broad Engineer filtering uses the order index plus filter. No new index or migration was justified by these maintained local plans; no cache/search engine was added. An earlier unmaintained bulk seed chose an order scan/sequence scan for rare filters (22.473/89.085ms); after explicit fixture maintenance, the existing GIN indexes were chosen. Other concurrent test activity and cache state differed between runs, so that comparison is diagnostic evidence, not a controlled causal performance claim.

These are short warmed local measurements, not an SLA, sustained-load test or production capacity claim. Dataset text is repetitive and its distribution is synthetic; short substrings can have poor selectivity; real distributions, cold caches, logging, network/TLS, concurrent ingestion, hardware and autovacuum may alter plans/results. Test representative production-like data before tuning. EXPLAIN adds measurement overhead. Later operations must monitor table statistics/GIN pending lists and preserve automatic maintenance, rather than force a planner choice or rebuild indexes on assumption.

## Read and operational review

Read queries use AsNoTracking and server-side WHERE/order/summary projection with LIMIT+1. Summary SELECT excludes description (description may still be evaluated for q filtering). List limit1..50 and bounded field/cursor sizes bound JSON output; detail descriptions are bounded by ingestion. First list page uses two SQL reads (watermark + bounded page), continuation one, detail one; there is no per-item database query, offset scan or count. Existing query SQL tests check parameterization/no description projection/no offset/count; real sort/commit-boundary tests remain separate.

New real integration evidence starts Kestrel with actual search controllers/DB/consumer, stops ONLY its fixture broker app, observes ingestion Backoff and health503 while list/detail/read-ready/live all stay200, then starts the broker and observes ingestion Running/200. A separate owned PG table lock verifies actual caller cancellation and command timeout, then successful reading after rollback. Existing graceful/forced-drain and quarantine tests remain in the28-case full integration suite.

The timeout experiment exposed Npgsql EF's InvalidOperationException wrapper around a transient Npgsql exception. Search now classifies that specific wrapper as503/database; unrelated unexpected errors remain safe500. Unit checks verify both cases without broad exception suppression. Logs never attach raw exceptions/queries/cursors/secrets; existing host safety and meter-listener low-cardinality tests pass. Metrics label only outcome/state/status class. No exporter is installed by this stage.

Manual quarantine diagnostics: inspect only the configured search-owned quarantine queue and safe failureCode/original message identity; message bytes belong there, never in logs. Quarantine can have duplicates after lost acceptance/ACK. Diagnose malformed/version/conflict/topology/permissions problems, then explicitly decide whether a valid event should be republished. No automatic replay, shared source queue purge or posting database investigation is implemented. See ../messaging.md for the confirmed-acceptance boundary and shutdown budget.

References: [PostgreSQL EXPLAIN](https://www.postgresql.org/docs/18/sql-explain.html), [VACUUM and pending GIN inserts](https://www.postgresql.org/docs/18/sql-vacuum.html), [Npgsql execution strategy](https://www.npgsql.org/efcore/api/Npgsql.EntityFrameworkCore.PostgreSQL.Storage.Internal.NpgsqlExecutionStrategy.html).
