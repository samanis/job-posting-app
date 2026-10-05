# Stage 3: posting-owned PostgreSQL persistence

Read `API/job-posting-api/shared-requirements.md` and `requirements-review.md` first. Inspect completed stages and applicable AGENTS.md. Implement only this stage; earlier numbered stages must be complete. Preserve unrelated work.

## Tasks

1. Add compatible EF Core 10/Npgsql dependencies and a posting-owned DbContext. Do not put posting/query shared tables into JobBoard.Persistence.
2. Map job records, idempotency ledger and outbox envelope/progress with required bounds, dates/UTC timestamps and unique constraints. Store digest/key scope, canonicalization version/fingerprint, stable saved response and event ID. Distinguish durable saved state from confirmed publication state.
3. Include outbox lease owner/generation/expiry, retry schedule/count and published timestamp; index due pending work. Configure optimistic/conditional state updates. Avoid storing secret connection values in migrations or source.
4. Create and commit at least one real migration and local dotnet-ef tool configuration. Expose safe migration commands; do not auto-migrate on every API replica startup.
5. Add transaction helpers capable of atomic job+ledger+outbox creation and consistent recovery after an ambiguous commit. Bound command/lock timeouts.
6. Use a real PostgreSQL container for integration tests: apply migrations to empty storage, validate mappings/checks/unique indexes, rollback atomically and round-trip complete records.

## Acceptance and checks

- Run the mandatory coverage gate: 100% lines, branches and methods for all authored production code, with source completeness and exclusions checked as required by shared-requirements.md. Keep unit/host coverage and external integration evidence accurately distinguished.

- Migration applies from empty database and required tables/indexes exist.
- Failure before commit leaves neither partial job nor orphan event/ledger.
- PostgreSQL checks independently preserve salary/date mappings; tests do not substitute EF InMemory for relational behavior.

Run appropriate build/tests and record actual results in `API/job-posting-api/ai-log/job-posting-api-work-notes.md`. Report changed files, assumptions and blockers. Do not commit, push or continue into later stages automatically.

