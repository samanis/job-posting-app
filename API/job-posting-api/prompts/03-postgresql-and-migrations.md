# Stage 3: posting-owned PostgreSQL persistence

Read `API/job-posting-api/prompts/shared-requirements.md` and `requirements-review.md` first. Inspect completed stages and applicable AGENTS.md. Implement only this stage; preserve unrelated work. Existing Stages 3-4 use the superseded ledger/outbox design: refactor them to these revised requirements before implementing dependent stages. Do not mistake earlier passing checks for verification of the revised design.

## Tasks

1. Use compatible stable EF Core 10/Npgsql and a posting-owned DbContext. Keep all application files under API/job-posting-api; do not introduce shared query storage.
2. Map one jobs table containing all saved fields, UTC createdAt, unique IdempotencyKeyDigest, RequestFingerprint, CanonicalizationVersion, stable response snapshot, stable EventId and nullable PublishedAt. These metadata fields are internal and must not leak into the saved-record DTO. There is no separate operation scope for this create-only API.
3. Remove IdempotencyRecord, its table, foreign keys and reads. Remove outbox entities/table, stored envelope, leases, generation, xmin publication fencing, schedule/count and due-work indexes. Keep ordinary job validation constraints and appropriate key indexes. Do not remove unrelated safeguards.
4. Provide a real migration and pinned local dotnet-ef configuration. Determine whether the old migration has been applied: preserve applied migration history and use an explicit upgrade/data-transfer migration when needed. Do not silently discard existing jobs or idempotency metadata, regenerate edited metadata as tool output, and update any exact hash-verified coverage exclusions honestly. Never auto-migrate every replica.
5. Implement bounded job insert, digest lookup, publication-status update and compensation delete by exact job identity. Never delete by key alone: a stale attempt must not remove a replacement row. Keep database transactions short and closed before broker IO. Preserve conservative uncertain-commit handling without automatically recreating a job.
6. Use real PostgreSQL integration tests for empty/upgrade migration as applicable, mappings/checks, unique digest concurrency, complete snapshots, publication metadata, atomic failed insert and exact-identity deletion.

## Acceptance and checks

- Run the mandatory 100% line/branch/method coverage gate with complete authored-source inclusion and unchanged justified generated-code exclusions. Report isolated unit/host coverage separately from external integration evidence.

- Only the jobs table supplies posting/idempotency state; no ledger or outbox remains.
- A failed insert leaves no partial job. Existing data is preserved or an explicitly approved disposable database is used.
- Required source, migrations, locks and tool/config files remain tracked; real relational behavior is not substituted with InMemory/SQLite.

Run appropriate build/tests and record actual results in `API/job-posting-api/ai-log/job-posting-api-work-notes.md`. Report changed files, assumptions and blockers. Do not commit, push or continue into later stages automatically.
