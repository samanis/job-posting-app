# Shared requirements: job posting API

Read this file and `requirements-review.md` before every stage. User instructions take priority over reference documents. Implement only the selected stage and reuse earlier work.

## Scope and working rules

- All backend applications and related infrastructure belong under `API/`, independently from the frontend; do not create or use a repository-root services folder. API-only phase: `API/job-posting-api/src/JobPosting.Api`, posting-owned persistence beneath it, `API/job-posting-api/tests/JobPosting.Api.Tests`, any posting integration-test project, API docs, `API/job-posting-api/JobBoard.slnx`, SDK/NuGet/build configuration, tools, and `API/job-posting-api/docker-compose.yml` with posting/PostgreSQL/RabbitMQ infrastructure. Prompts stay in `API/job-posting-api/`. Run .NET and Compose commands from `API/job-posting-api` so its SDK/configuration apply. This user-directed layout overrides the PDF's original repository-root Compose placement.
- The posting application root is exclusively `API/job-posting-api`: all its source, tests, tooling, persistence, work notes and infrastructure must remain beneath that same folder. The existing `src/JobBoard.Persistence` placeholder is also inside it; source under src participates in the production coverage gate. Do not recreate separate API-level JobPosting.Api, JobBoard.Persistence, posting tests or tools folders. Preserve the existing one-service design unless a separate production project has a concrete reason; folder consolidation alone does not authorize shared database storage.
- Do not implement `JobSearch.Api`, a RabbitMQ consumer, the query database, Angular changes, authentication, job management, or a shared persistence library for both services. Existing empty folder names are not a mandate to use shared storage. Preserve the uncommitted Angular Material work.
- Inspect files and applicable AGENTS.md first. Use .NET 10 stable, EF Core 10 and compatible stable packages; verify installed/documented APIs. Avoid unnecessary project layering, generic repositories, mediator/event-bus frameworks or extra infrastructure. Feature-oriented folders in one service are sufficient.
- Record truthful changes, tests, assumptions and blockers in `API/job-posting-api/ai-log/job-posting-api-work-notes.md`. Work notes are not a fabricated transcript. The PDF's genuine transcript export remains a separate submission obligation.
- Do not automatically commit, push, deploy, publish, send messages, or run subsequent prompts. Run actual stage checks and report failures honestly.
- Include manifests, lock/config/tool files, migrations, Dockerfiles and safe examples in Git. Ignore .NET `bin/obj`, results and local secrets, not required source or migration files. Keep existing app ignore protections.

## HTTP and validation contract

POST `/api/jobs`; JSON request contains `title`, `department`, `location`, `description`, `salaryMin`, `salaryMax`, `closingDate`; require one valid `Idempotency-Key` header as specified in the review. Do not generate a fallback key.

Use independently validated server DTOs: required fields must distinguish missing/null salary fields from valid zero; decimal JSON numbers, not strings; date-only valid ISO calendar date; normalized text and bounded lengths/amounts as specified. Reject NaN/infinity, numeric overflow, excess precision, malformed JSON/date, and minimum >= maximum. UTC `createdAt`, generated string UUID `id`, PostgreSQL numeric/date/timestamptz mappings. Do not use floating-point salary arithmetic or UTC conversion of closingDate.

Business timezone is configured (default America/Toronto), checked at startup; time is injectable. Validate future date for NEW work only. After parsing/canonicalizing an existing-key request, check its fingerprint and replay/resume before applying current-date validation: a previously valid closing date can expire after acceptance.

Responses:

| Condition | Response |
| --- | --- |
| New or replayed acceptance: job committed, matching event confirmed and publication state committed | 202; same authoritative saved-record body on replay |
| Malformed JSON, invalid header/shape/type | 400 safe Problem Details; useful `errors` where applicable |
| Semantic field validation | 422 ValidationProblemDetails; errors keyed by field names |
| Same key, different canonical payload | 409 with top-level `code: idempotency_key_conflict` |
| Concurrent owner still processing same key after bounded wait | 409 with `code: idempotency_in_progress` and Retry-After |
| Job saved but publication/confirmed-state recording unresolved | 503 with `code: publication_pending`, Retry-After, safe same-key retry guidance |
| Required database unavailable or commit outcome uncertain | 503 with `code: dependency_unavailable`, same-key retry guidance; never claim rollback without proof |
| Unhandled exception | 500 generic Problem Details plus traceId; full diagnostics only in server logs |

Example 202 body: all seven normalized saved fields at the top level, `id`, `createdAt`, `status: accepted`, and `message: Your job posting has been saved and sent for processing. It may take a few moments to appear in search results.` Store stable response values; replay the original body, including timestamp, without republishing a known-completed event. No claim that search visibility has been verified. No body-less success or nonexistent Location URI.

## Durable acceptance workflow

1. Parse, normalize and fingerprint the request. Resolve a matching ledger record first; new keys receive semantic validation before writes.
2. One PostgreSQL transaction creates the job, idempotency record/fingerprint/response snapshot and a stable versioned event in the outbox. Unique constraints are the authority, not check-then-insert or process-local locks. Handle concurrent unique conflicts in a new usable transaction and resolve the existing record.
3. Commit before contacting RabbitMQ. Never hold a database transaction open while waiting for broker network IO.
4. Request path and hosted recovery dispatcher share one durable claiming/publication mechanism. Acquire an atomic bounded lease with owner token/claim generation; no double ownership among replicas. Renew when needed, recover expired claims, and use conditional updates/fencing to prevent stale owners from marking/overwriting newer work. Skip other owners after bounded waits.
5. Publish the exact stored event through a broker-neutral producer. Require publisher confirmation AND no mandatory-routing return. Nack, return, timeout, cancellation, disconnection, or uncertain result never marks it published.
6. After acknowledgement, durably mark the event published under the valid lease. Only then return 202. If recording this state fails, return an uncertain retryable failure; re-publication may be necessary and must reuse the event ID.
7. Dispatcher continuously recovers unconfirmed pending events with bounded backoff. A background confirmation can satisfy a later same-key POST replay. A failed original request does not cancel durable recovery or justify a fresh job/key.

Retain an idempotency ledger and event identity for the life of each job. Published outbox payload cleanup is only a documented future operation, not a stage requirement. Pending/poison messages must remain inspectable, alertable and recoverable; do not silently drop them after a retry count.

## Messaging boundary

Broker-neutral `IJobEventPublisher` accepts immutable event envelope plus CancellationToken and completes successfully only on confirmed broker acceptance; it does not expose RabbitMQ channels/properties outside the adapter. Avoid abstractions that imply every broker has identical topology/guarantees.

Envelope: `eventId`, `eventType: JobPostingCreated`, `schemaVersion: 1`, `occurredAt` UTC, `correlationId`, and `job` with the complete authoritative saved record. A correlation identifier is diagnostic metadata, not a dedupe key. Serialize once and persist the envelope; retries reuse identity/content even across restarts.

RabbitMQ adapter uses stable compatible RabbitMQ.Client APIs, reusable managed connection, safe channel concurrency, persistent JSON messages, durable direct exchange `job-post-exchange`, routing key `job-posting.created.v1`, durable queue `job-post-queue` and binding. Local single-node durable queue/volume is sufficient; document how HA differs. Topology can be changed by configuration within this adapter. The future search consumer is responsible for idempotent projection by eventId; do not implement it now.

Exactly-once job creation is scoped to a key under the retained database ledger. At-least-once publication is intentional. Test the confirmation-before-state-write crash window; never present publisher confirms as eliminating duplicate delivery or proving consumer completion.

## Nonfunctional requirements

- Stateless across requests/replicas: no session affinity, local durable business files, or in-memory idempotency authority. Database rows own all recoverable progress. Pools, clocks, bounded breaker state and connection objects are allowed local resources.
- Central exception handling (`IExceptionHandler`/Problem Details or supported equivalent), request trace correlation, one structured logging configuration. Suppress stack traces, SQL, hostnames, connection strings, payloads and credentials in production HTTP responses. Avoid logging full job descriptions or raw idempotency keys; use digest/correlation fields. Allow explicit safe validation text.
- Use a non-HTTP Polly/.NET resilience pipeline around RabbitMQ publication. Circuit breaker shared by request/dispatcher within an instance; bounded confirm timeout/retries, cancellation propagation and background backoff. Never wrap the entire POST or recreate jobs on retry. Cancellation is not a transient retryable error. Permanent topology/authorization/serialization failures need diagnostics, not hot retry loops.
- Initial configurable publishing budget: <=10 seconds overall for request, <=3 seconds per confirmation, <=2 immediate attempts, jitter, breaker failure ratio 0.5/sample 60s/minimum throughput 3/break 15s. Account for time spent claiming; document total server/request bounds relative to the client 15-second timeout. Avoid nested retry policies exceeding budgets.
- Graceful SIGTERM: stop accepting/claiming new work, drain in-flight requests and publications within HostOptions shutdown timeout (30s), release or expire safe leases, retain unknown results, then close channels/connections. Docker stop grace 45s. Crash recovery must work even when graceful shutdown is impossible.
- Liveness checks process health without external IO; readiness reflects database and publisher capability for this strict response contract. An open breaker can make readiness degraded/unready but must not be treated as liveness failure or disable recovery. Broker recovery must reconnect/probe so readiness can recover. Bound health-check IO.
- JSON logs and metrics: request duration/outcome, duplicate/replay/conflict counts, outbox pending count/oldest age, publish attempts/failures/latency, breaker transitions, shutdown status. Keep high-cardinality IDs in logs/traces, not metric labels. No separate collector/dashboard required.
- Bounded request size, database commands, outbox batches, leases and worker concurrency; async IO; DI scopes per request/worker batch; validate nonsecret configuration at startup. No secrets committed. Restrict local Docker exposed ports to loopback. Angular proxy works without broad CORS; add allowlisted CORS only if a documented direct-browser deployment requires it.

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

Use meaningful unit tests plus ASP.NET integration tests with actual PostgreSQL and RabbitMQ containers. EF InMemory/SQLite cannot prove PostgreSQL locking, uniqueness, migrations or broker confirms. Use deterministic clocks/gates/controllable failures; avoid long sleeps. Tests need isolation, cleanup and bounded waits.

Required scenarios: valid acceptance/saved-record replay; all field/shape boundaries; same key same/different payload including concurrency across hosts; late replay after closing date; database failure/ambiguous commit; broker down, nack/unroutable/confirmation timeout; recovery after restart; crash after confirm before marking; concurrent dispatchers and stale leases; production-safe errors/logs; low-traffic breaker open/half-open/closed; SIGTERM during publish; no duplicate job or lost recoverable event.

`API/job-posting-api/docker-compose.yml` starts posting API, PostgreSQL, RabbitMQ and a safe one-shot migration service if selected, with durable named volumes and health checks. Keep Dockerfiles, .dockerignore, environment examples and broker/database settings under API/job-posting-api too. Do not run concurrent migrations automatically in every API replica. Multi-stage .NET 10 Dockerfile runs as non-root, uses exec-form entrypoint and validated SIGTERM behavior. Pin verified stable image/package versions, not floating `latest`.

Document Windows/macOS/Linux commands: prerequisites, restore/build/test, migrations, one Compose command from API/job-posting-api, local `dotnet run`, environment overrides, POST/replay/conflict examples, recovery/backlog checks and graceful stop. `API/job-posting-api/.env.example` may contain clearly labeled local-only defaults; real .env/credentials remain ignored. A clean checkout must include all build/run inputs. Explicitly list deferred search/client integration and do not claim full PDF completion.
