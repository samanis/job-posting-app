# Search-owned PostgreSQL persistence (Stage 3)

Independent search EF Core/Npgsql code, connection, database, user and migrations live here. No posting assembly/source/database/HTTP dependency. PostgreSQL setup/migrations are explicit; registration/startup validates options and builds a context factory without connecting. No HTTP query endpoint or RabbitMQ consumer is implemented in this stage.

## Configuration and migration

SearchDatabase settings: ConnectionString defaults Host=localhost;Port=5433;Database=job_search;Username=jobsearch (nonsecret, distinct local search defaults). CommandTimeoutSeconds10 allowed1..30, LockTimeoutMilliseconds3000 allowed1..5000, IdleTransactionTimeoutSeconds15 allowed1..30. Connection timeout5s; SQL statement/lock/idle limits set per session. Validation names settings without echoing values. IncludeErrorDetail/sensitive logging/detailed EF errors disabled; EF/Npgsql logs suppressed. No execution-strategy write retries.

Provision a NEW search-owned database/user and grant migration privileges, including pg_trgm installation (or have an administrator preinstall this trusted extension). Runtime role needs jobs SELECT/INSERT and sequence USAGE; separate migration/runtime roles can be configured externally. Never use posting credentials/tables. Existing DB contents require inspected migration history, not automatic deletion/recreation.

From API/job-search.api, configure SearchDatabase__ConnectionString with search host/port/database/user and supply the password through secret configuration or PGPASSWORD; do not embed secrets in committed commands. Then:

```sh
dotnet restore JobSearch.slnx --locked-mode
dotnet tool restore
dotnet ef database update --project src/JobSearch.Api
dotnet ef migrations has-pending-model-changes --project src/JobSearch.Api
```

Design-time factory reads SearchDatabase environment settings, never posting config. Migration InitialSearch creates ONLY jobs plus job_ingestion_sequence and pg_trgm extension. Its Down removes search jobs/sequence; use only with explicit data-loss authorization/backups, not ordinary stop. Leave applied migration history intact; future changes get new migrations. Development Docker orchestration remains Stage8; real tests need only Docker and create their own databases.

## Projection and durable identity

SearchJob stores source job UUID(primary key), unique source event UUID, canonical payload hash/version, full seven job fields, source UTC created/occurred timestamps and commit-ordered ingestion sequence. PostgreSQL timestamptz has microsecond precision; source UTC ticks are also retained in bigint fields so ToJob reconstructs the original100ns created timestamp and original occurred ticks remain available. Hash represents the original validated event, including precise source identity/time; no clock or correlation field affects it.

ProjectionStore accepts a successfully validated JobCreatedEvent (DTO construction alone is not validation). ProjectAsync opens a short transaction, acquires search-owned transaction advisory lock(1785620787,1), reads either identity with AsNoTracking, and returns Duplicate only for exactly one row with matching job/event/hash/version. Different content, same job/new event, same event/different job, or crossed identities produce Conflict without overwriting. Distinct job/event IDs with identical content remain distinct. No ledger/inbox/outbox exists.

For new identity: allocate sequence AFTER the lock, insert, save, commit once, return Inserted. The lock stays held through commit; all cooperating projection writers follow this rule, so a later committed row cannot hide a lower-sequence pending row under an earlier watermark. Normal reads do not acquire the advisory lock. ReadWatermarkAsync returns highest COMMITTED row sequence (0 if empty), not sequence last_value. Gaps from rolled-back inserts are permitted. Direct database insert tools must obey this write protocol; uniqueness/positive-value constraints alone cannot enforce commit order. No insert default silently bypasses it.

A known job/event unique violation disposes the failed context/transaction, then reads fresh durable state to classify Duplicate/Conflict; missing state rethrows. An exception from CommitAsync is uncertain, not a proved rollback. After disposal, one fresh read can establish an exact duplicate; missing/conflicting/unreadable state throws and never authorizes acknowledgment. Cancellation can prevent reconciliation and leaves outcome uncertain. No automatic insert retry or repair. Failed save/lock/rollback/disposal throws; it cannot report success. Stage4 will ACK only Inserted or verified Duplicate, and quarantine Conflict.

## Constraints and read indexes

Nonempty UUIDs, unique event/sequence, positive sequence,64-character lowerhex hash/version1, required bounded nonblank text, date0001..9999, source tick range, precise nonnegative min<max salaries<=999999999.99. Salaries use unbounded numeric with scale<=2 CHECK instead of numeric(p,2) rounding. Reader enforces UTF16 limits; PostgreSQL varchar character limits are a defense, not a replacement for validation.

B-tree newest(created_at DESC,id DESC), closing(closing_date ASC,created_at DESC,id DESC); GIN gin_trgm_ops on title/description/department/location. Index creation and extension verified in real PostgreSQL. Query plans/performance are Stage7, not claimed from schema alone. Source timestamps preserve original ticks; later read contracts format milliseconds and must choose consistent sorting/paging tuples.

## Verification

```sh
dotnet run --project tools/CoverageGate
dotnet build JobSearch.slnx --configuration Release --no-restore
dotnet test tests/JobSearch.Api.IntegrationTests --configuration Release --no-build --no-restore
```

Integration fixtures own GUID-named PostgreSQL18.6 containers and random search databases/credentials; cleanup removes only those resources. No developer/posting DB is migrated. External tests prove fresh migration/extension/indexes, complete precision roundtrip, independent-provider races, identity conflicts, rollback/constraints, simulated lost commit acknowledgment AFTER actual commit, bounded lock/cancellation and an uncommitted write blocking later allocation while the read watermark stays0. This is injected commit acknowledgment loss, not a claim of a real network drop.

Isolated unit tests cover configuration/model/generated SQL/migration Up and Down, ordered transaction calls, duplicate/conflict/failure/uncertainty and fresh reads. SQL translation tests suppress connection opening and supply synthetic readers through EF interceptors; they are not real PostgreSQL evidence. Unit/host coverage stays separately measured100%; two unmodified EF designer/snapshot files are hash-checked generated-only exclusions. Authored migration code remains covered.

References: [Npgsql index mapping](https://www.npgsql.org/efcore/modeling/indexes.html), [PostgreSQL transaction advisory locks](https://www.postgresql.org/docs/18/explicit-locking.html). Versions pinned NpgsqlEF10.0.3/EFDesign10.0.4/tool10.0.4 form a verified .NET10 baseline, not latest-version claims.
