# Read performance

This report measures the search API's read endpoints on a local machine. It covers the effect of response caching and the database query plans behind each search.

## How to run the workload

Run from `API/job-search.api`. Docker must be running with Linux containers.

```powershell
dotnet restore JobSearch.slnx --locked-mode
dotnet run --project tools/ReadWorkload --configuration Release -- 10000 4 30 uncached
dotnet run --project tools/ReadWorkload --configuration Release -- 10000 4 30 cached
```

| Argument | Meaning | Range | Default |
|---|---|---|---|
| 1 | Jobs to create | 1,000–100,000 | 10,000 |
| 2 | Concurrent clients | 1–32 | 4 |
| 3 | Duration in seconds | 1–60 | 10 |
| 4 | `cached` or `uncached` | — | `cached` |

The tool starts its own throwaway PostgreSQL container and removes it afterwards. It never connects to an existing database. It fills the database with synthetic jobs, then runs the API in the same process.

Each run writes `cache-cached.json` or `cache-uncached.json` in this folder. A report includes the machine, runtime, latency percentiles, error counts and full query plans.

## Cache comparison (2026-10-06)

Both runs used 10,000 jobs, four concurrent clients and 30 seconds.

The traffic mix was:

- 80% across six popular searches and job details
- 10% numbered text searches
- 10% rotating job details

Popular requests were warmed up before timing. Caching follows the rules in [search.md](../search.md#caching).

**p50, p95 and p99** are percentiles. For example, p95 means 95% of requests were faster than this value.

| Measurement | Uncached | Output cache |
|---|---:|---:|
| Requests | 14,100 | 232,915 |
| Cache hit rate | 0% | 99.09% |
| Database queries | 24,913 | 4,120 |
| Database queries per request | 1.767 | 0.0177 |
| p50 latency | 7.20 ms | 0.21 ms |
| p95 latency | 20.33 ms | 1.12 ms |
| p99 latency | 25.90 ms | 7.55 ms |
| Requests per second | 469.70 | 7,760.74 |
| Errors | 0 | 0 |

Sources: [cache-uncached.json](cache-uncached.json) and [cache-cached.json](cache-cached.json).

### Caveats

- **The workload is synthetic.** It is not production traffic. The 99% hit rate is not a forecast.
- **Requests repeat.** Every group of requests eventually repeats. The clients also overlap.
- **Closed loop.** Each client sends its next request as soon as the last one finishes. A faster API therefore produces more requests and more repeats.
- **Not included:** paging with cursors, new jobs arriving during the run, logging, a real network and TLS.
- **Midnight is avoided.** The test clock stays away from midnight. Separate HTTP tests check expiry and the midnight rule.
- **Local machine.** The client and the API share one process. These numbers show the benefit of caching repeated reads. They are not a capacity guarantee.

## Query plans (2026-10-05)

An earlier 10-second run without caching recorded how PostgreSQL executes each search. It used `EXPLAIN ANALYZE` on the exact queries the API sends.

Results: 4,146 requests, 414 per second, p50 8.19 ms, p95 21.32 ms, p99 25.64 ms, no errors. Source: [latest.json](latest.json).

| Query | Time | Index used |
|---|---:|---|
| Newest, no filter | 0.052 ms | Newest-first order index |
| Closing soon, no filter | 0.055 ms | Closing-date order index |
| Newest, rare word in titles | 0.550 ms | Title and description trigram indexes |
| Closing soon, rare word in descriptions | 1.326 ms | Title and description trigram indexes |
| Newest, common word "Engineer" | 0.485 ms | Newest-first order index, then a filter |

A **trigram index** splits text into three-letter pieces. It lets PostgreSQL find "contains" matches without reading every row.

- Searches without filters read the matching order index directly.
- Rare words use the trigram indexes, then sort a few rows.
- Common words walk the order index and filter as they go.

These plans needed no new index.

Plan choice depends on table statistics. Before statistics were refreshed, rare-word searches scanned the table instead and took 22.5 ms and 89.1 ms. Other conditions also differed between those runs, so this is a hint, not proof. Production should keep automatic `VACUUM` and `ANALYZE` running.

The data is repetitive and synthetic. Real data, cold caches or other hardware may produce different plans. Test with realistic data before tuning.

## Decision: no Redis for now

Keep the in-memory output cache. Nothing yet shows a need for Redis:

- There is no target number of API instances.
- There is no measured cache memory pressure.
- There is no evidence that instances repeat each other's work.

A shared Redis cache would make sense once several instances clearly duplicate work, or cache size becomes a measured limit. Copying all jobs into Redis for search would make sense only if realistic uncached queries show PostgreSQL is the bottleneck.

Any Redis change must be tested for Redis outages, cache key versioning, and the same cursor and midnight rules.

References: [PostgreSQL EXPLAIN](https://www.postgresql.org/docs/18/sql-explain.html), [VACUUM and pending GIN inserts](https://www.postgresql.org/docs/18/sql-vacuum.html).
