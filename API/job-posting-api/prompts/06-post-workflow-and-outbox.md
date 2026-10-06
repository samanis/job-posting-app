# Stage 6: POST, direct publication and compensating deletion

Read `API/job-posting-api/prompts/shared-requirements.md` and `requirements-review.md` first. Inspect completed stages and applicable AGENTS.md. Implement only this stage; preserve unrelated work. Existing Stages 3-4 use the superseded ledger/outbox design: refactor them to these revised requirements before implementing dependent stages. Do not mistake earlier passing checks for verification of the revised design.

## Tasks

1. Wire POST /api/jobs to strict parsing, server validation and job-row idempotency. Commit the new job before direct broker publication; do not hold a transaction open across network IO. The filename is retained for existing references, but no outbox is implemented.
2. Only the request that inserted the new job publishes it. Matching duplicates read job publication status: published returns original 202 snapshot, unresolved returns 409 idempotency_in_progress while processing or 503 publication_unresolved when completion cannot be established. Use bounded waits; do not republish or introduce owner leases/dispatcher.
3. After broker confirmation and successful mandatory routing, record PublishedAt and return 202 with original saved fields/id/createdAt/status/message. This internal status write supports duplicate responses and does not depend on a consumer.
4. On any publication exception, including unknown acceptance and cancellation, attempt deletion of that exact saved job under an independent bounded cleanup token (initial 3 seconds). After successful deletion throw a publication exception for central safe HTTP mapping. Do not imply broker messages have been retracted.
5. If deletion fails or its outcome is uncertain, emit Critical (the .NET Fatal equivalent) with job/event IDs and both publication/cleanup failures in server diagnostics, then throw a cleanup-failure exception. Production responses stay generic. No automatic cleanup retry, background scanner or recovery endpoint.
6. If broker acceptance is confirmed but recording PublishedAt fails, keep the job, log Critical and return a safe publication_unresolved failure. Do not delete or republish confirmed work. The publication flag is required for safe successful duplicate replay; explain this database failure window.
7. Test compensation success, cleanup error/timeout/cancellation, safe centralized exception mapping, duplicate races during publish, completed response loss, confirmed-before-status failure and unknown-acceptance cleanup. Use separate unit/host and real PostgreSQL/RabbitMQ evidence.

## Acceptance and checks

- Run the mandatory 100% line/branch/method coverage gate with complete authored-source inclusion and unchanged justified generated-code exclusions. Report isolated unit/host coverage separately from external integration evidence.

- Every 202 has a saved job and recorded confirmed/routable broker acceptance; it does not promise search processing.
- Publication failure attempts exact-job deletion; cleanup failure emits Critical and never returns 202.
- No ledger, outbox, hosted recovery, leases, automatic message retry or compensation retry is introduced.
- Document that process crashes may bypass cleanup/logging and uncertain broker acceptance may leave queued messages after deletion.

Run appropriate build/tests and record actual results in `API/job-posting-api/ai-log/job-posting-api-work-notes.md`. Report changed files, assumptions and blockers. Do not commit, push or continue into later stages automatically.
