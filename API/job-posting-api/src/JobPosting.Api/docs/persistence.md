# Posting-owned PostgreSQL persistence (Stage 3)

All source, migrations, tests, tooling and configuration belong to `API/job-posting-api`. Run the commands below from that application root. The DbContext is `src/JobPosting.Api/Persistence/PostingDbContext.cs`; the existing JobBoard.Persistence placeholder is not a shared posting/search database implementation.

Revised Stage 3 implements one job table and persistence helpers. Revised Stage 4 now distinguishes creators, completed replay and unresolved duplicates. POST `/api/jobs` coordinates direct RabbitMQ publication and compensating deletion; no outbox or recovery dispatcher is planned.

## Dependencies and configuration

Pinned compatible baseline: Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3 (and Npgsql 10.0.3), EF Core/Design 10.0.4 and local dotnet-ef 10.0.4. Npgsql 10.0.3 requires EF Core >=10.0.4; these pins are compatible tested versions, not a claim that every package/runtime is the latest servicing release. Lockfiles and `.config/dotnet-tools.json` are required checkout inputs.

```sh
dotnet restore JobBoard.slnx --locked-mode
dotnet tool restore
dotnet build JobBoard.slnx --configuration Release --no-restore
dotnet run --project tools/CoverageGate
```

The API can still start and the isolated/host coverage command can run without a database: startup validates settings but does not contact PostgreSQL or auto-migrate. Real persistence/integration tests require Docker with Linux containers. `dotnet test JobBoard.slnx` now includes that separate integration project; use `dotnet test tests/JobPosting.Api.Tests` for just unit/host cases.

Runtime configuration section is `PostingDatabase`, using normal ASP.NET configuration. Design-time migration configuration reads the same section from environment variables, with the documented defaults below. Set environment overrides explicitly when migrating a nondefault database; do not assume host-specific appsettings changes are read by the design-time factory.

| Setting | Default | Bound |
| --- | --- | --- |
| `ConnectionString` | `Host=localhost;Port=5432;Database=job_postings;Username=jobposting` (no password) | Parseable, explicit host/database/username |
| `CommandTimeoutSeconds` | 10 | 1–30 |
| `LockTimeoutMilliseconds` | 3000 | 1–5000 |
| `IdleTransactionTimeoutSeconds` | 15 | 1–30 |

Connection timeout is fixed at 5 seconds. The provider applies command timeout plus PostgreSQL session `statement_timeout`, `lock_timeout` and `idle_in_transaction_session_timeout`, including administrative migration connections. Sensitive EF logging/detailed errors and Npgsql error detail are disabled. Validation failures identify configuration keys, never connection values. PostgreSQL `Options` in a supplied connection string is replaced by these bounded settings. No execution-strategy write retries are enabled: retrying a whole save after an uncertain commit could create another identity.

Npgsql's process-wide `DisableDateTimeInfinityConversions` switch is set before provider use. This preserves literal DateOnly endpoints (0001-01-01/9999-12-31) and prevents PostgreSQL infinity from masquerading as a valid .NET date. Configure this persistence module before opening any Npgsql connection in the process, as done by the host, design factory and integration fixture.

## Local PostgreSQL and explicit migration

The integration suite creates and removes its own database container. For a persistent local database, use the root Compose service and its migration container from the repository root:

```sh
docker compose up -d postgres job-posting-migrate
```

Alternatively, apply migrations from this application folder with the EF Core tool (PowerShell shown; use `export` on macOS/Linux):

```powershell
dotnet tool restore
$env:PostingDatabase__ConnectionString = 'Host=127.0.0.1;Port=5432;Database=job_postings;Username=jobposting'
$env:PGPASSWORD = 'local-development-only-change-me'
dotnet ef database update --project src/JobPosting.Api
```

Use external configuration or secret management for real credentials; never put them in a migration, tracked connection file or command example. Migration is an explicit administrative step; API replicas never run migrations automatically. `docker compose stop` keeps the data volume; do not delete volumes for routine stopping.

Schema verification and review:

```sh
dotnet ef migrations has-pending-model-changes --project src/JobPosting.Api
dotnet ef migrations script --idempotent --project src/JobPosting.Api
```

The original migration `20261005172328_InitialPosting` is preserved unchanged. The new `20261005203934_JobRowIdempotency` migration transfers existing ledger digests/fingerprints/version/response/event IDs and outbox publication timestamps into jobs, then removes the ledger/outbox tables. All existing job fields remain unchanged. Jobs lacking complete old metadata abort the migration atomically rather than receiving invented identities. Tests cover both populated upgrades and empty installs; no user database was migrated during this change.

Back up an existing database and review the generated SQL before upgrading. Removed envelope/lease/retry data is intentionally not retained under the new scope. Old pending jobs remain unpublished; there is no automatic delivery recovery. The new migration is forward-only: Down throws with backup-restoration guidance because removed envelopes cannot be reconstructed honestly. Do not silently rewrite applied history or drop a user's database.

## Single-table schema

`jobs` contains the authoritative saved fields plus internal metadata:

| Column | Purpose |
| --- | --- |
| idempotency_key_digest | Unique, case-sensitive-key SHA-256 digest (64 lowercase hex characters) |
| request_fingerprint / canonicalization_version | Versioned canonical payload identity |
| response_json | Exact original accepted-response text, bounded JSON object matching job ID/status |
| event_id | Nonempty unique stable publication identity |
| published_at | Nullable finite UTC broker-acceptance timestamp, not earlier than created_at |

No operation scope, separate ledger/outbox, foreign keys to these removed tables, xmin fencing, leases or due-work indexes remain in the current model. Historical migrations still describe the old model for upgrade compatibility.

Text/salary/date safeguards remain: nonblank bounded text, unrestricted numeric plus precision/range/order checks (typmod rounding must not hide invalid salaries), literal date bounds and finite created timestamp. New closing dates are validated in application code; the schema does not reject later replay based on CURRENT_DATE. Metadata does not appear in the saved-record DTO.

## Helpers and boundaries

`PendingPosting.Create` builds a single job and stable saved response. Creation timestamps are normalized to PostgreSQL microsecond precision before serialization. No event envelope is stored; Stage 5 will build it from the job when publishing.

`PostingWriteStore.CreateAsync` inserts the job inside a short transaction. Commit exceptions remain explicitly uncertain; no execution retry is enabled. `ReadAsync` uses a fresh context and digest query, returning the original job or null. Absence is not proof of rollback. Revised Stage 4 uses these helpers with the creator/duplicate contract described in idempotency.md.

`MarkPublishedAsync` accepts exact job/event IDs, preserves an already recorded timestamp and returns false for missing/wrong identity. `DeleteUnpublishedAsync` removes only that exact unpublished identity. A deleted job's replacement uses a different job/event ID, so an old cleanup cannot remove it. These helpers are request-path primitives, not a cleanup worker, public delete endpoint or broker-confirmation mechanism. Publication, marking and compensation are not an atomic cross-system operation. Stage 6 uses one publishing creator and independent three-second cleanup cancellation. See [POST workflow](post-workflow.md).

## Verification

From the application root:

```sh
dotnet run --project tools/CoverageGate
dotnet test tests/JobPosting.Api.IntegrationTests --configuration Release --no-restore
dotnet build JobBoard.slnx --configuration Release --no-restore
```

Revised Stage 3 checks passed: 163 isolated unit tests (763/763 lines, 194/194 branches, 119/119 methods across 28 authored files/31 types), 16 host tests (42/42 lines, 4/4 branches, 1/1 methods) and 21 real PostgreSQL tests. Integration execution cannot contribute to unit coverage. Real tests cover empty/populated upgrade, rollback of incomplete-metadata migration, complete snapshots, salary/text/date checks, duplicate rollback, exact-identity compensation, publication metadata and retained coordinator concurrency/commit regressions. Tests own their temporary containers/databases and clean up afterward.

The coverage gate includes both authored migration Up/Down implementations. Only three exact unmodified EF metadata files are excluded: the original designer, new designer and current generated model snapshot. `coverage-exclusions.json` verifies their normalized text hashes (CRLF/LF and UTF-8 BOM handled consistently); coverage properties list those exact filenames. Microsoft OpenAPI's unmodified generated obj output remains excluded. No authored source was excluded or threshold lowered.
