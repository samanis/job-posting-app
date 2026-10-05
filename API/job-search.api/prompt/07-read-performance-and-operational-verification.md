# Read workload and observability

Read shared-requirements.md and requirements-review.md in this prompt directory first. Apply every shared constraint. Execute only this stage after previous stages have passed; preserve unrelated work.

## Tasks and acceptance

Review high-volume read behavior using representative varied data: AsNoTracking, SQL-only filtering/projection, bounded page/body, appropriate indexes, cancellation/timeouts, no N+1 or description fetch for summaries. Run EXPLAIN ANALYZE for both order modes and realistic substring filters; tune only with evidence and retain migration history. Add a reproducible bounded workload tool under tools with seed size/concurrency/duration/environment and p50/p95/p99/throughput/error counts. Keep data/load in isolated owned resources, never stress or mutate user DB. Do not invent latency/SLA success targets from PDF or add caches/Elasticsearch without demonstrated need.

Confirm search GET remains usable during broker outage, while ingestion health shows outage and later recovery. Verify graceful/forced consumer shutdown, canceled query behavior and safe error/metric cardinality. Document local performance limitations and manual quarantine diagnostics.

Acceptance: locked build, coverage100%, actual query plans and measured load results, unit/host/external evidence separated, no claimed production capacity from local figures.

Run appropriate build/tests and mandatory exact 100% coverage gates. Write actual commands/results and remaining limitations in API/job-search.api/ai-log/job-search-api-work-notes.md. Do not proceed to the next stage automatically.
