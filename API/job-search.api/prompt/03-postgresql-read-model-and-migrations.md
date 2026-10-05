# Search-owned persistence and atomic deduplication

Read shared-requirements.md and requirements-review.md in this prompt directory first. Apply every shared constraint. Execute only this stage after previous stages have passed; preserve unrelated work.

## Tasks and acceptance

Implement EF Core/Npgsql with independent search connection/database/user and jobs table containing full source job fields, event UUID, canonical hash/version, commit-ordered ingestion sequence. Unique job/event constraints, data constraints, precise numeric mapping/date-only/UTC fields, B-tree sorting indexes and pg_trgm extension/GIN substring indexes. Explicit initial migration and tool manifest; no startup automatic migration, no posting database access. Implement short atomic projection insert, unique-race fresh lookup, exact duplicate no-op versus identity/content conflict. Use a transaction-scoped search-owned advisory lock only for ingestion watermark allocation/commit ordering; bound lock/command timeouts. Rollback errors and uncertain commit must never be mistaken for success. No inbox ledger.

Acceptance: real isolated PG fresh migrations, all fields roundtrip, independent-instance same-event races one row, same job/new event conflict, same event/different job conflict, different jobs same content allowed, rollback/uncertain commit and commit-ordered watermark concurrent-query tests. Verify indexes/extension and no pending model changes. Unit gates100%, integration separate.

Run appropriate build/tests and mandatory exact 100% coverage gates. Write actual commands/results and remaining limitations in API/job-search.api/ai-log/job-search-api-work-notes.md. Do not proceed to the next stage automatically.
