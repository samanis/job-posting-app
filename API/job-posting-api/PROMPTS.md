# Job posting API implementation prompts

Run these prompts individually, in order, using their repository-relative paths. All posting source, persistence, tests, tools and configuration are under API/job-posting-api; run .NET and Compose commands from that application directory. These files are implementation instructions, not implemented backend capabilities.

Start with: `Run API/job-posting-api/01-dotnet10-foundation.md`.

Read [requirements-review.md](requirements-review.md) for the review of the PDF, design image, and user requirements. Every implementation prompt also requires [shared-requirements.md](shared-requirements.md).

1. [Foundation](01-dotnet10-foundation.md): .NET 10 project, configuration, centralized diagnostics, test setup.
2. [Contract and validation](02-contract-and-validation.md): request/response definitions and independent server validation.
3. [PostgreSQL persistence](03-postgresql-and-migrations.md): EF Core mappings, transaction boundaries, committed migrations.
4. [Durable idempotency](04-durable-idempotency.md): concurrent requests, canonical fingerprints, replay and recovery.
5. [RabbitMQ producer](05-rabbitmq-producer.md): broker abstraction, topology, confirmed publication.
6. [POST coordination and outbox](06-post-workflow-and-outbox.md): endpoint, synchronous confirmation gate and background recovery.
7. [Resilience and operations](07-resilience-and-graceful-shutdown.md): circuit breaker, bounded retries, shutdown, health and observability.
8. [Docker development](08-docker-and-local-development.md): API-local Compose, migrations, durable volumes and reproducible setup.
9. [Verification and documentation](09-integration-verification-and-handoff.md): failure/concurrency tests, API docs and client handoff.

Execute only the selected stage. Do not build the search API or change either Angular client as part of these prompts. Stage 9 must document the existing client's incompatible interpretation of 202 for a later explicitly scoped client integration task.

Every stage must pass the mandatory 100% line/branch/method coverage gate in shared-requirements.md. The stage 1 retrofit is complete: run `dotnet run --project tools/CoverageGate` from `API/job-posting-api` and extend it with each stage. Keep isolated unit, in-process host/component, and external integration results accurately identified.

The approved success gate is **PostgreSQL committed + RabbitMQ publisher confirmation + publication state durably recorded**, then HTTP 202. An outbox commit alone does not satisfy the gate.
