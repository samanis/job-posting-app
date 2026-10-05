# Stage 7: bounded resilience, diagnostics and shutdown

Read `API/job-posting-api/shared-requirements.md` and `requirements-review.md` first. Inspect completed stages and applicable AGENTS.md. Implement only this stage; preserve unrelated work. Revised Stages 3-6 already implement job-row idempotency and direct publication/compensation. Verify that baseline before adding this stage; do not reintroduce the superseded ledger/outbox design.

## Tasks

1. Implement a non-HTTP circuit breaker around direct RabbitMQ publication with a bounded confirmation timeout, cancellation propagation and one publish attempt per new job. Never retry the POST, transaction, message or cleanup automatically.
2. Validate configurable initial bounds: overall publish budget <=10 seconds, confirm timeout <=3 seconds, breaker failure ratio 0.5/sample 60s/minimum throughput 3/break 15s. Test low-volume closed/open/half-open/closed behavior. Connection reconnect/probe may restore capability but must not replay jobs.
3. Complete central safe exception mapping and JSON structured logging. Critical cleanup logs contain job/event IDs, publication and deletion failure context; suppress credentials, raw keys, full payload and descriptions. Unexpected Production errors expose only safe messages/traceId.
4. Implement bounded /health/live without external IO and /health/ready reflecting database/publisher capability. Add request/publication/compensation/breaker metrics without high-cardinality labels. Remove backlog/oldest-event/dispatcher metrics.
5. Drain in-flight requests/publications/compensation within 30s HostOptions shutdown timeout, then dispose resources. Cleanup gets its own bounded token even if request cancellation occurs, but must fit the shutdown budget. No leases or recovery worker.
6. Test SIGTERM during publication/cleanup and forced termination. Document that forced stop can leave unresolved rows without a Fatal log; readiness and process restart do not automatically repair business state.

## Acceptance and checks

- Run the mandatory 100% line/branch/method coverage gate with complete authored-source inclusion and unchanged justified generated-code exclusions. Report isolated unit/host coverage separately from external integration evidence.

- Circuit breaker/timeout obey measured budgets, fail safely and introduce no automatic retry.
- Production diagnostics are safe; compensation failure is distinguishable in server logs.
- Graceful shutdown drains bounded work; crash/unknown-publication gaps are explicitly accepted, not claimed recovered.

Run appropriate build/tests and record actual results in `API/job-posting-api/ai-log/job-posting-api-work-notes.md`. Report changed files, assumptions and blockers. Do not commit, push or continue into later stages automatically.
