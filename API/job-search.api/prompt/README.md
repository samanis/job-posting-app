# Job search API implementation prompts

These are implementation instructions only. No search API has been built by this prompt-authoring task. Run stages one at a time in order; all application files will stay under API/job-search.api.

Read [requirements review](requirements-review.md) and [shared requirements](shared-requirements.md) before each stage. User decisions and design assumptions are distinguished from the PDF's broader full-stack submission requirements.

1. [Foundation and isolated application](01-dotnet10-foundation.md)
2. [Independent contracts and validation](02-event-and-query-contracts.md)
3. [Search-owned persistence and atomic deduplication](03-postgresql-read-model-and-migrations.md)
4. [RabbitMQ consumption with commit-before-ACK](04-rabbitmq-consumer-and-projection.md)
5. [Poison handling and bounded recovery](05-failure-handling-and-consumer-lifecycle.md)
6. [Indexed read API and cursor paging](06-search-and-detail-endpoints.md)
7. [Read workload and observability](07-read-performance-and-operational-verification.md)
8. [Independent Docker and existing-broker setup](08-docker-and-local-development.md)
9. [Final verification and search client handoff](09-integration-verification-and-handoff.md)

Start with: `Run API/job-search.api/prompt/01-dotnet10-foundation.md`.

Search owns its PostgreSQL projection and code. The only posting interoperability boundary is the versioned RabbitMQ wire contract; there are no posting project/database/HTTP dependencies. Exact100% unit and separate bootstrap host coverage is required at every stage, with independent external integration evidence.
