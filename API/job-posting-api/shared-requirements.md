> Current implementation status: revised Stages 3-9 are implemented and verified, including POST, confirmed publication/compensation, resilience and the Docker/local posting subset. Stage 9 verification and client handoff are complete; see docs/verification-and-handoff.md and docs/client-integration-handoff.md. Historical revision statements below describe the earlier transition, not the current code.


# Shared requirements: simplified job posting API

Read this and requirements-review.md before every stage. Latest user decisions override earlier architecture and reference documents.

## Revision status

The user approved prompt updates to replace the ledger/outbox architecture with job-row idempotency, direct publication and compensating deletion. Revised Stages 3-4 replaced the old design and were reverified before dependent Stages 5-8. No automatic business retry/recovery, dispatcher, publication leases, outbox or separate ledger is in scope.

## Scope and working rules

- All backend applications and related infrastructure belong under `API/`, independently from the frontend; do not create or use a repository-root services folder. API-only phase: `API/job-posting-api/src/JobPosting.Api`, posting-owned persistence beneath it, `API/job-posting-api/tests/JobPosting.Api.Tests`, any posting integration-test project, API docs, `API/job-posting-api/JobBoard.slnx`, SDK/NuGet/build configuration, tools, and `API/job-posting-api/docker-compose.yml` with posting/PostgreSQL/RabbitMQ infrastructure. Prompts stay in `API/job-posting-api/`. Run .NET and Compose commands from `API/job-posting-api` so its SDK/configuration apply. This user-directed layout overrides the PDF's original repository-root Compose placement.
- The posting application root is exclusively `API/job-posting-api`: all its source, tests, tooling, persistence, work notes and infrastructure must remain beneath that same folder. The existing `src/JobBoard.Persistence` placeholder is also inside it; source under src participates in the production coverage gate. Do not recreate separate API-level JobPosting.Api, JobBoard.Persistence, posting tests or tools folders. Preserve the existing one-service design unless a separate production project has a concrete reason; folder consolidation alone does not authorize shared database storage.
- Do not implement `JobSearch.Api`, a RabbitMQ consumer, the query database, Angular changes, authentication, job management, or a shared persistence library for both services. Existing empty folder names are not a mandate to use shared storage. Preserve the uncommitted Angular Material work.
- Inspect files and applicable AGENTS.md first. Use .NET 10 stable, EF Core 10 and compatible stable packages; verify installed/documented APIs. Avoid unnecessary project layering, generic repositories, mediator/event-bus frameworks or extra infrastructure. Feature-oriented folders in one service are sufficient.
- Record truthful changes, tests, assumptions and blockers in `API/job-posting-api/ai-log/job-posting-api-work-notes.md`. Work notes are not a fabricated transcript. The PDF's genuine transcript export remains a separate submission obligation.
- Do not automatically commit, push, deploy, publish, send messages, or run subsequent prompts. Run actual stage checks and report failures honestly.
- Include manifests, lock/config/tool files, migrations, Dockerfiles and safe examples in Git. Ignore .NET `bin/obj`, results and local secrets, not required source or migration files. Keep existing app ignore protections.

## HTTP and validation contract

POST `/api/jobs` accepts title, department, location, description, salaryMin, salaryMax and closingDate. Require one opaque Idempotency-Key on the FIRST request: 1-128 ASCII letters/digits/underscore/hyphen. The UI generates it; the API does not invent a fallback. Keys are case-sensitive submission identities, not authorization or content deduplication.

Retain strict parsing and independent server validation: nullable input salaries distinguish missing/null from zero; decimal JSON numbers, exact precision <=2, nonnegative <=999999999.99 and min < max; valid date-only YYYY-MM-DD; trimmed required text bounded at 200/100/100/10000. Reject unsupported/duplicate/unknown shapes and fields, overflow and invalid dates. No floating-point salary calculations or invented currency. Preserve plain description content. Configure/validate business timezone (America/Toronto default), injectable time and bounded request size.

Hash the exact UTF-8 key with SHA-256 as lowercase hex. Canonicalize all seven normalized fields with fixed order, invariant two-place salary/date representations and a version, then SHA-256 fingerprint. Exclude current time, generated IDs and createdAt. Persist digest, fingerprint/version, stable response snapshot, EventId and PublishedAt on the job. Use one unique digest constraint; no operation scope, separate ledger or outbox. Generated metadata stays internal, outside response DTO.

| Condition | HTTP contract |
| --- | --- |
| New success or known completed same-key replay | 202 only after job commit, confirmed/routable broker acceptance and recorded PublishedAt; original saved response |
| Malformed JSON/header/shape/type | 400 safe Problem Details |
| Semantic validation | 422 field-keyed ValidationProblemDetails |
| Same key, different fingerprint | 409 `idempotency_key_conflict` |
| Bounded creation/processing contention | 409 `idempotency_in_progress`, Retry-After: 1 |
| Publication failed and compensation completed | 503 `publication_failed`; no success claim |
| Publication status unresolved or compensation failed/uncertain | 503 `publication_unresolved`; safe guidance, no automatic repair |
| Database unavailable or commit outcome uncertain | 503 `dependency_unavailable`; preserve same-key retry guidance |
| Unexpected application failure | 500 generic Problem Details with traceId |

202 body contains all seven normalized saved fields, id, UTC createdAt, status: accepted and message: Your job posting has been saved and sent for processing. It may take a few moments to appear in search results. Replay stable original content, never fabricate a Location/status URI or wait for search completion.

## Simplified workflow

1. Strictly parse, normalize and fingerprint. Look up job by key digest BEFORE future-date validation. Matching published job replays original response without another insert/publish; differing fingerprint conflicts. Matching unpublished job returns bounded in-progress/unresolved failure and does not automatically republish.
2. Validate future closing date only for a new key. Insert the job and internal metadata in one short PostgreSQL transaction. On unique conflict, dispose the failed transaction and read the winning job in a fresh context. No in-memory lock, reservation mechanism or publication lease. Bound command/lock waits.
3. Commit before broker IO. A commit exception may be uncertain: a fresh read may establish the outcome; absence cannot prove rollback. Never automatically retry the whole transaction or direct callers to replace a known unresolved attempt's key.
4. Only the request that created the job directly publishes its immutable envelope once. Require publisher confirmation AND no mandatory-routing return. No consumer acknowledgment or dependency on the search service.
5. After confirmed acceptance, record PublishedAt, then 202. If status persistence fails, retain the job, log Critical and return publication_unresolved; never retract/delete confirmed work or automatically republish it.
6. On publication exception (including cancellation or unknown acceptance), attempt exact-job compensation deletion with an independent bounded token, initially 3 seconds. Then throw the publication exception for central safe HTTP mapping. Do not hold a DB transaction during publish or claim deletion retracts broker messages.
7. If deletion fails or its commit outcome is uncertain, log Critical (.NET Fatal equivalent) with job/event IDs and publication plus cleanup failures, then throw cleanup-failure exception. No automatic cleanup retries or worker.

Remaining job rows retain their digest for their lifetime. Invalid requests consume no key. Compensation deletion removes the key with the job; a later attempt can recreate it and may duplicate a message previously accepted without observed confirmation. No TTL/cleanup of successful rows is in scope.

## Messaging boundary

Broker-neutral IJobEventPublisher accepts immutable versioned envelope and CancellationToken, completing only on confirmed/routable acceptance. Keep RabbitMQ types/topology in the adapter. Envelope: eventId, eventType JobPostingCreated, schemaVersion 1, occurredAt UTC, correlationId and complete authoritative saved job. Build it from the saved job; no separately persisted envelope or delivery schedule.

Use compatible stable RabbitMQ.Client, reused managed connection, safe channel concurrency, persistent JSON, durable direct job-post-exchange, routing key job-posting.created.v1, durable job-post-queue and binding. Local single-node volume durability is development infrastructure, not HA. Connection repair may restore broker capability but must not scan or replay saved jobs. No consumer implementation.

## Accepted limitations

Compensation is not an atomic rollback across PostgreSQL and RabbitMQ. Broker acceptance followed by lost confirmation can cause job deletion while the message is queued. Recreation can duplicate delivery. A crash can bypass deletion and Critical logging and leave unresolved records; restart provides no automatic business recovery. Same-key repeats prevent duplicates for retained rows but do not repair unknown publication. Manual investigation is required. Do not promise exactly-once, eventual automatic delivery, guaranteed cleanup or a Fatal log after forced termination.

## Nonfunctional requirements

- Stateless request handling across replicas: no session affinity, local durable files or in-memory idempotency authority. Database unique rows decide creation; connection pools/clocks/breaker resources may be local.
- Central exception handling/Problem Details, structured JSON ILogger and correlation. Production responses expose no stack traces, SQL, hosts, payloads, credentials or connection strings. Do not routinely log descriptions/raw keys. Critical compensation logs include safe identity/cause context and sanitized diagnostics.
- Non-HTTP circuit breaker around one direct publish attempt: publish budget <=10s, confirm timeout <=3s, failure ratio 0.5/sample 60s/minimum throughput 3/break 15s, validated/configurable. No whole-request, message, transaction or cleanup automatic retry. Cancellation is not an outage. Half-open probes/connection repair do not replay jobs.
- SIGTERM drains requests/publications/cleanup within HostOptions 30s, then disposes channels/connections. Docker stop grace 45s. Cleanup token is independent of request cancellation but bounded by remaining shutdown time. No lease release or dispatcher recovery.
- Bounded liveness with no external IO; readiness reflects DB/publisher capability. Broker reconnect/probe can recover capability without repairing business records. No backlog health/metrics. Measure duration/outcomes/publication/compensation/breaker transitions without high-cardinality metric labels.
- Validate nonsecret settings, bound async SQL/network operations, keep scopes per request, no secrets committed. Local exposed ports loopback; use Angular proxy, not broad CORS. No public GET/status/update/delete/search/auth endpoints in this phase.

## Verification and reproducibility

### Mandatory 100% coverage

- User requirement: 100% unit-test coverage. Enforce 100% **lines, branches and methods** (the .NET equivalent of functions) for every authored production C# file/type in the posting API and any posting-owned production projects. If the chosen instrumenter reports statement coverage separately, require 100% there too. Do not invent a statement metric that the tool does not measure.
- Set up a .NET 10-compatible coverage collector/instrumenter and a reproducible command that fails on missing coverage or any metric below 100%. Verify actual supported APIs and threshold behavior; commit its configuration and package/tool lockfiles. Enforce per-file/type thresholds as well as aggregate totals so one fully covered area cannot mask another. A report without a failing threshold is insufficient.
- Instrument the complete authored production assembly/source set, including untouched files, middleware, exception handlers, startup/bootstrap, configuration, validation, application coordination, persistence adapters and broker adapters where instrumentable. Missing files/types, missing or empty reports, and accidentally filtering all production code must fail the gate. Verify expected-source completeness, not only reported executed code.
- Isolated unit tests must exercise production logic through observable behavior and deterministic dependency seams. Keep real PostgreSQL/RabbitMQ integration tests as a separate correctness gate; their coverage cannot substitute for or raise the unit-suite score. Explicitly distinguish isolated unit tests from in-process host/component tests in reports. Exercise startup/bootstrap through in-process host tests when isolation is impractical; report that coverage honestly rather than calling every WebApplicationFactory test a unit test.
- Exclusions are limited to dependencies, test code, declarations and genuinely tool-generated, unmodified artifacts (for example generated EF migration designer/snapshot code). List exact exclusions and reasons. Authored/modified migration logic remains in scope. Do not exclude production files just because they are difficult to test, or hide authored code using ExcludeFromCodeCoverage, suppression pragmas, disabled tests, lowered thresholds or rounded percentages.
- Test behavior and meaningful failure paths, not private implementation details or tautological replicas of production code. Coverage is execution evidence, not proof of correctness. If a compiler-generated construct cannot be instrumented reliably, record the exact limitation and resolve the tooling/code design; do not silently declare 100% or invent an exclusion.
- Run and pass the coverage gate at **every stage**, including a retrofit of the already-created stage 1 foundation. Stage 1's previously passing 16 tests have not established coverage and must not be described as 100% covered until the new gate measures and passes it. Preserve completed implementation while adding the coverage setup and genuinely missing tests.
- The foundation now provides `dotnet run --project tools/CoverageGate` from `API/job-posting-api`. Reuse and extend this gate as production projects/types are added; keep new application/adapter logic in the isolated unit scope and only API bootstrap in the host scope. Retain source/type completeness and negative checks, and update recorded metrics after every stage.
- Final verification must demonstrate that the gate rejects an intentional below-threshold result and rejects a missing/empty report without changing committed thresholds. Record measured numerators/denominators and report locations in work notes, and document the exact developer/CI command.

Use real PostgreSQL/RabbitMQ integration evidence separately from unit/host coverage. Test validation, key races, completed replay after expiry, uncertain commit, publish failures/unknown acceptance, exact-job compensation and cleanup failure/timeout, status-write failure after confirmation, low-volume circuit transitions and graceful/forced stop. Do not require a recoverable outbox or claim crash repair.

API/job-posting-api/docker-compose.yml starts posting, PostgreSQL and RabbitMQ with durable volumes/health checks and explicit administrative migrations or a one-shot service. Non-root exec-form multi-stage .NET10 image, verified pinned versions, all build/run inputs tracked and real secrets ignored. No replica auto-migrations or fake search service.

Document Windows/macOS/Linux restore/build/test, migration/Compose/local run commands, key-on-first-request examples, duplicate/conflict/compensation behavior, manual investigation and graceful stop. Preserve user volumes. Explain missing client/search integration and genuine transcript export as separate obligations.
