# Stage 5: broker abstraction and RabbitMQ producer

Read `API/job-posting-api/shared-requirements.md` and `requirements-review.md` first. Inspect completed stages and applicable AGENTS.md. Implement only this stage; earlier numbered stages must be complete. Preserve unrelated work.

## Tasks

1. Define a narrow broker-neutral IJobEventPublisher accepting immutable versioned JobPostingCreated envelope and cancellation token. Complete only on confirmed/routable acceptance. Keep RabbitMQ types and topology entirely within the adapter.
2. Add a compatible stable RabbitMQ.Client dependency and use its actual documented async connection/channel/confirmation APIs. Manage connection reuse, channel ownership/concurrency, reconnect and disposal without a connection per HTTP request.
3. Configure durable job-post-exchange, routing key job-posting.created.v1, durable job-post-queue and binding. Publish persistent JSON with stable eventId/message ID, content type, schema version and correlation metadata.
4. Combine publisher confirms with mandatory routing and returned-message handling. Ack of an unroutable message is not success; do not swallow nack/return/timeout or mark published from socket-write completion.
5. Keep the envelope serialized in PostgreSQL so delivery attempts reuse exact event content. Document the at-least-once guarantee and the future consumer's eventId deduplication obligation.
6. Add real RabbitMQ integration tests for correct envelope/topology/persistence flags, nack/return/connection loss/cancel/timeout as feasible. Exercise failure classification separately with deterministic adapter seams where actual broker nack injection is impractical. No consumer implementation.

## Acceptance and checks

- Run the mandatory coverage gate: 100% lines, branches and methods for all authored production code, with source completeness and exclusions checked as required by shared-requirements.md. Keep unit/host coverage and external integration evidence accurately distinguished.

- A test publication reaches the intended durable queue with the expected immutable envelope and confirmed result.
- Mandatory unroutable publication cannot be reported successful.
- Connection/channel use is safe under concurrent requests and reusable after recovery.
- Application contracts do not import RabbitMQ types; no claim of exactly-once or completed search indexing.

Run appropriate build/tests and record actual results in `API/job-posting-api/ai-log/job-posting-api-work-notes.md`. Report changed files, assumptions and blockers. Do not commit, push or continue into later stages automatically.

