# Stage 6: POST endpoint, confirmed acceptance and outbox recovery

Read `API/job-posting-api/shared-requirements.md` and `requirements-review.md` first. Inspect completed stages and applicable AGENTS.md. Implement only this stage; earlier numbered stages must be complete. Preserve unrelated work.

## Tasks

1. Wire POST /api/jobs to validated DTO, durable idempotency and persisted job/outbox creation. Commit PostgreSQL before publisher IO; never hold transaction locks during RabbitMQ confirmation waits.
2. Build one publication coordinator for the request path and hosted dispatcher. Atomic durable claim/lease/generation and conditional state writes must avoid overlapping owners across replicas. Bound wait time, recover expired claims and fence stale owners.
3. Attempt confirmed mandatory publication of the stored event. After successful acknowledgement, durably record published status under valid ownership. Only when that durable state exists return 202 with the full saved-record acceptance snapshot.
4. Handle broker failure after database commit as 503 publication_pending with Retry-After and safe same-key retry guidance; do not roll back/delete a saved job, invent another key, or respond 202 merely because an outbox row exists.
5. Matching retries resume pending publication or replay the original 202 snapshot after confirmation. If another owner has work, bounded wait then 409 idempotency_in_progress. Confirmed replay must not publish again.
6. Implement a scoped hosted dispatcher that claims due rows in bounded batches, publishes with the same coordinator and retries durably after crashes/outages. Retain and surface poison/configuration failures rather than deleting events.
7. Cover acknowledgement-before-state-write crashes, lost response after completed acceptance, DB failure marking publication, stale owners and concurrent HTTP/worker attempts. Duplicates reuse eventId; no duplicate job and no lost pending event.

## Acceptance and checks

- Run the mandatory coverage gate: 100% lines, branches and methods for all authored production code, with source completeness and exclusions checked as required by shared-requirements.md. Keep unit/host coverage and external integration evidence accurately distinguished.

- Every 202 has a durable job and durably recorded RabbitMQ acceptance; response contains the actual saved fields/id/createdAt.
- Broker down creates a durable pending job/event, returns 503, then restart/recovery/same-key replay converges to 202 and one job.
- At-least-once replay after ambiguous publication uses the identical event identity.
- Endpoint/status/error contract tests pass without modifying the Angular client or search API.

Run appropriate build/tests and record actual results in `API/job-posting-api/ai-log/job-posting-api-work-notes.md`. Report changed files, assumptions and blockers. Do not commit, push or continue into later stages automatically.

