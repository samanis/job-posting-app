# Backend APIs

Two independent .NET 10 APIs. Each owns its own source, database, migrations, tests and Dockerfile.

- [job-posting-api](job-posting-api/README.md): write side. Validates and stores job postings, then publishes `JobPostingCreated` to RabbitMQ.
- [job-search.api](job-search.api/README.md): read side. Consumes those events into a search read model and serves list and detail queries.

They share no code or database; the only contract between them is the event on RabbitMQ. To run both together with PostgreSQL and RabbitMQ, use `docker compose up --build` from the repository root. See the [root README](../README.md).
