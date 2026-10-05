# Stage 7: resilience, diagnostics and graceful shutdown

Read `API/job-posting-api/shared-requirements.md` and `requirements-review.md` first. Inspect completed stages and applicable AGENTS.md. Implement only this stage; earlier numbered stages must be complete. Preserve unrelated work.

## Tasks

1. Add a shared non-HTTP resilience pipeline around RabbitMQ publishing: low-volume circuit breaker, bounded timeout and at most two immediate attempts within the documented request budget. Never apply retry to the entire POST transaction or count cancellation as an outage.
2. Validate/configure sampling/minimum throughput/break duration/backoff; test closed/open/half-open/closed progression and broker reconnect/probe when no traffic arrives. Background exponential retry must use durable schedule and avoid hot loops.
3. Complete centralized exception/error mapping and structured logs with request/trace/job/event correlation, safe key digests, dependency outcome/duration and breaker transitions. No full job payload/description, raw keys, passwords or connection strings in routine logs or Production responses.
4. Add bounded /health/live and /health/ready: DB/publisher capability for readiness only, no external dependencies for liveness. Readiness or open circuits must not disable dispatcher recovery. Provide basic counters and outbox count/oldest age without high-cardinality metric labels.
5. Implement cancellation-aware drain on SIGTERM: stop new claims, let in-flight requests/publish finish within 30s host shutdown, preserve unknown outcomes, fence or expire leases, then dispose resources. Set ordering explicitly; DB cancellation after commit must not erase durable work.
6. Exercise graceful stop and abrupt termination during commit/publication, pending-work recovery, production diagnostics redaction and bounded worker errors. Keep safe operational errors distinct from validation errors.

## Acceptance and checks

- Run the mandatory coverage gate: 100% lines, branches and methods for all authored production code, with source completeness and exclusions checked as required by shared-requirements.md. Keep unit/host coverage and external integration evidence accurately distinguished.

- Circuit breaker opens under low posting volume, fails fast, probes and recovers; retries/timeouts obey a single measured budget.
- Production responses contain no diagnostic internals and logs correlate without exposing secrets.
- Health states are correct under DB/broker loss and later recovery.
- Shutdown drains or leaves replay-safe durable state; restart does not create duplicate jobs or lose an event.

Run appropriate build/tests and record actual results in `API/job-posting-api/ai-log/job-posting-api-work-notes.md`. Report changed files, assumptions and blockers. Do not commit, push or continue into later stages automatically.

