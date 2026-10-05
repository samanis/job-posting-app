# Backend applications

Each backend application owns its source, persistence, tests, tools, configuration and infrastructure within its own folder.

- [job-posting-api](job-posting-api/README.md): implemented .NET 10 Stages 1-8 with POST, confirmed RabbitMQ publication, compensation, resilience, shutdown and Docker/local setup, including job-row idempotency storage, all posting tests/tools/prompts, PostgreSQL migrations and SDK/build settings.
- `JobSearch.Api/`: existing unimplemented search placeholder, outside the posting application.

Frontend applications remain independent under `../apps/`. Use the [posting application guide](job-posting-api/README.md) for build, run and verification commands.
