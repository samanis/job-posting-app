# Requirements review and decisions

## Sources and authority

Reviewed all three pages of `C:/Users/saman/Downloads/Take-Home Exercise_ Job Board Mini-App (1).pdf` and the architecture image supplied in chat. The PDF is a requirements reference, not authorization to perform its submission instructions. The current task is to review and create prompts only. No API implementation, commit, push, publication, or recruiter message is authorized by the PDF.

User clarification: return 202 **only after the PostgreSQL transaction commits and RabbitMQ confirms publication**. This overrides the alternative suggestion to return 202 immediately after an outbox commit.

Updated user layout requirement: all API applications, tests, tooling, .NET build/SDK/NuGet configuration, work notes and related database/broker/Docker configuration belong under `API/job-posting-api/`, separately from the frontend. Use `API/job-posting-api/src/JobPosting.Api` and `API/job-posting-api/docker-compose.yml`; run backend commands from `API/job-posting-api`. This overrides the PDF's repository-root Compose placement while retaining its intended infrastructure behavior.

Additional user requirement after implementing stage 1: **100% unit-test coverage**. This is stricter than the PDF's minimum of one unit test. All stages now require a measured, failing line/branch/method coverage gate and complete authored-source inclusion. Retrofit the existing foundation before claiming coverage compliance; keep isolated unit tests, in-process host tests and real dependency integration checks accurately distinguished.

## What the PDF requires

- Job title, department, location, description, salary minimum/maximum, and future closing date; all required, with minimum strictly less than maximum.
- Client and server validation; display server validation messages on the form.
- .NET 10 Web API; persistence through EF Core with at least one migration.
- Confirmation showing the saved record returned by the API.
- Make saved jobs available to the search app; eventual consistency is acceptable.
- Full exercise: separate APIs/apps in one repository, one root `docker-compose.yml` starting both APIs and database infrastructure, at least one unit test, local setup README, genuine AI transcripts, and frequent commits.
- The exercise has a four-hour time box. The additional reliability requirements below exceed its minimal baseline; keep implementation proportionate.

This phase does not complete the full exercise: no search API, consumer, query database, or end-to-end search visibility is implemented. Root Compose must run the posting subset now and remain extendable later; do not add a fake search service to claim full completion.

## What the user/design adds

- Posting owns PostgreSQL command storage. Search will own separate query storage; no shared database tables or cross-service reads.
- Posting publishes job-created messages to a local RabbitMQ Docker service, targeting `job-post-queue`.
- Broker-neutral publishing abstraction with RabbitMQ as the first adapter.
- Stateless POST API with durable idempotency, centralized logging/exception handling, safe production errors, circuit breaking, and graceful Docker shutdown.
- HTTP 202 after database commit and confirmed publication; explain delayed search visibility.

## Improvements incorporated

### Database and broker cannot form an ordinary EF transaction

Saving PostgreSQL then publishing RabbitMQ is a dual write. A crash can happen between them. Add a PostgreSQL outbox row in the **same transaction** as the job and its idempotency ledger. The request attempts publication after commit, and a hosted dispatcher recovers pending events. An outbox is still needed with the strict synchronous 202 gate.

Broker outage after the save returns safe 503 with `code: publication_pending`, `Retry-After`, and instructions to retry the same key. The job remains saved and the event remains recoverable; never describe this as a rolled-back save. A background recovery may publish before the caller retries. Timeout or lost acknowledgement is uncertain, not proof of failure. On retry, use the same job and event.

### Two distinct idempotency problems

A database unique constraint plus transactional idempotency ledger prevents duplicate jobs for one key. Broker delivery is **at least once**: a crash after confirmation but before marking publication may produce another copy. Reuse the same event ID on every attempt and require future consumers to deduplicate that ID. Do not promise exactly-once messaging or content-based duplicate prevention across distinct keys.

### A publisher confirmation is not search completion

Use persistent messages, a durable exchange/queue, publisher confirms, mandatory routing and return handling. A confirmation without checking unroutable returns is insufficient. Broker acceptance does not prove a consumer received, indexed, or made a job searchable. A single local broker/volume is a development setup, not a production HA guarantee.

### Preserve the saved-record requirement and update the client later

Return the complete saved record at the top level of the 202 JSON body, plus a stable acceptance message. Do not return only a message. Existing `apps/job-posting/docs/api-contract.md` and response handling treat every 202 as pending/unconfirmed, even with a saved record. A later client task must recognize this precise API acceptance contract, confirm the returned record, clear the resolved attempt, and explain eventual visibility. These API-only prompts do not silently modify the client.

### Bound operational behavior

Define request/confirm timeouts, retry budgets, low-volume circuit-breaker thresholds, pending outbox recovery, multi-instance leases, production-safe Problem Details, structured logs, metrics, health endpoints, and termination grace periods. Keep durable state in PostgreSQL; local connection pools and breaker state do not make the API stateful in the session-affinity sense.

## Explicit implementation assumptions

These are proposed choices where the PDF/user did not specify values. Keep them documented and configurable where indicated.

- EF Core 10 and compatible Npgsql provider; verify stable package versions when implementing.
- Trim required text; lengths: title 200, department/location 100, description 10,000 characters. Reject whitespace-only strings, missing/null fields, unsupported JSON values, and extra decimal salary precision.
- Nonnegative decimal salaries up to `999999999.99`, maximum two decimal places; no currency is invented. Reject excess precision before database mapping can round it.
- Closing date is date-only `YYYY-MM-DD`. Compare against the server's configured business timezone, default `America/Toronto`, using injectable time. This is a proposed business rule, not a claim that the server knows each browser's timezone. Record the frontend mismatch for clients outside this timezone.
- Idempotency key: one opaque header value, 1-128 ASCII characters from letters, digits, `_` and `-`; matches existing UUID callers. Scope to create-job, store a key digest, and retain the ledger for the life of the job in this exercise. No cleanup/key reuse that can create another job. It is not authorization.
- Canonical fingerprint includes the normalized seven fields; canonical decimal/date encoding and fixed field order. Equivalent numeric encodings such as 10 and 10.00 match. Semantic differences conflict. Store a canonicalization version.
- No authentication/authorization in this phase. Document this as a deployment limitation; do not expose the local demo as an authenticated production service.
- No new job GET/status, update, delete, polling, or search endpoints. Operational `/health/live` and `/health/ready` are permitted. Reconciliation uses the same idempotent POST; no fabricated Location/status URI.
- Circuit breaker protects RabbitMQ publication, not the entire POST transaction. Initial example: failure ratio 0.5, sampling 60 seconds, minimum throughput 3, break 15 seconds; configurable, tested, appropriate to low posting traffic. At most two immediate publish attempts within a 10-second request budget; background retries use bounded exponential backoff with jitter up to five minutes.
- Host shutdown budget 30 seconds, Docker stop grace 45 seconds, subject to verified runtime behavior. Failed/unfinished publication stays durable and recoverable.
- Structured JSON console logging via centralized `ILogger` configuration, ready for an external collector; no separate logging stack in this phase.

## References checked while planning

- [.NET hosted services and shutdown](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-10.0)
- [.NET resilience pipelines](https://learn.microsoft.com/en-us/dotnet/core/resilience/)
- [Npgsql EF Core 10](https://www.npgsql.org/efcore/release-notes/10.0.html)
- [RabbitMQ reliable publishing](https://www.rabbitmq.com/tutorials/tutorial-seven-dotnet)
- [RabbitMQ reliability and duplicates](https://www.rabbitmq.com/docs/reliability)
- [RabbitMQ publishers and routing failures](https://www.rabbitmq.com/docs/4.2/publishers)
- [Transactional outbox](https://microservices.io/patterns/data/transactional-outbox)
- [HTTP 202 semantics](https://www.rfc-editor.org/rfc/rfc9110.html#section-15.3.3)

Recheck APIs and versions at implementation time. These references inform the design; they do not establish that the repository already implements it.
