# Stage 5: broker abstraction and direct RabbitMQ producer

Read `API/job-posting-api/shared-requirements.md` and `requirements-review.md` first. Inspect completed stages and applicable AGENTS.md. Implement only this stage; preserve unrelated work. Existing Stages 3-4 use the superseded ledger/outbox design: refactor them to these revised requirements before implementing dependent stages. Do not mistake earlier passing checks for verification of the revised design.

## Tasks

1. Define narrow broker-neutral IJobEventPublisher accepting immutable versioned JobPostingCreated envelope and CancellationToken. Complete only when RabbitMQ confirms broker acceptance and mandatory routing succeeds; never wait for consumer acknowledgment or search visibility.
2. Use compatible stable RabbitMQ.Client documented async APIs. Reuse managed connections with safe channel ownership/concurrency, connection repair and disposal. Connection repair is not business-message recovery.
3. Configure durable direct job-post-exchange, routing key job-posting.created.v1, durable job-post-queue and binding. Use persistent JSON, stable job EventId as message ID, schema/content-type and correlation metadata.
4. Treat nack, return, timeout, cancellation or connection loss as failures; socket-write completion is not acceptance. Distinguish definitive failure from unknown acceptance for diagnostics, but both enter the agreed compensation policy.
5. Build the envelope from the saved job and its stable EventId for direct request-path publication. Do not persist a separate envelope/outbox or add dispatcher, message retry pipeline or replay publishing. At-most-once effects are not guaranteed: acknowledgment loss and job deletion/recreation can duplicate delivery.
6. Use real RabbitMQ tests for correct routing/envelope/persistence and unroutable failure; deterministic seams cover nack/timeout/cancel/lost acknowledgment where broker injection is impractical. Do not implement a consumer.

## Acceptance and checks

- Run the mandatory 100% line/branch/method coverage gate with complete authored-source inclusion and unchanged justified generated-code exclusions. Report isolated unit/host coverage separately from external integration evidence.

- Confirmed/routable publication succeeds without any search service running.
- Unroutable or unconfirmed publication cannot report success.
- No RabbitMQ types escape the adapter and no exactly-once guarantee is claimed.

Run appropriate build/tests and record actual results in `API/job-posting-api/ai-log/job-posting-api-work-notes.md`. Report changed files, assumptions and blockers. Do not commit, push or continue into later stages automatically.
