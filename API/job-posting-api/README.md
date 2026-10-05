> **Revision status:** Stages 1-9 are implemented and verified. POST returns 202 only after PostgreSQL commit, confirmed/routable RabbitMQ publication and durable publication status. Circuit breaking, health, metrics and bounded graceful shutdown are implemented. The Docker/local posting subset is implemented. Stage 9 verification and client handoff are complete; see docs/verification-and-handoff.md and docs/client-integration-handoff.md.


# Job posting API application

All source and supporting files for the job posting API live together here:

```text
job-posting-api/
  src/JobPosting.Api/
  src/JobBoard.Persistence/
  tests/JobPosting.Api.Tests/
  tools/CoverageGate/
  ai-log/
  JobBoard.slnx
  global.json
  Directory.Build.props
  NuGet.Config
  01-dotnet10-foundation.md ... 09-integration-verification-and-handoff.md
```

`src/JobPosting.Api` is the C# project within this application. Source, persistence, tests, tools, prompts and configuration belong to the same application root. Frontends and the unimplemented search API remain separate.

From the repository root:

```sh
cd API/job-posting-api
dotnet restore JobBoard.slnx --locked-mode
dotnet build JobBoard.slnx --no-restore
dotnet run --project tools/CoverageGate
dotnet run --project src/JobPosting.Api --launch-profile http
```

Run backend commands from this directory so its SDK/configuration apply. See the [service guide](src/JobPosting.Api/README.md), [API contract](src/JobPosting.Api/docs/api-contract.md) and [prompt index](PROMPTS.md).

Stages 1-9 are implemented, including posting-owned PostgreSQL persistence/migrations and durable idempotency. See the [persistence guide](src/JobPosting.Api/docs/persistence.md) for explicit migration commands and the separate real PostgreSQL test command (requires Docker), and the [idempotency guide](src/JobPosting.Api/docs/idempotency.md) for concurrent requests, replay and uncertain commit behavior. POST `/api/jobs` and direct publication/compensation are implemented. Resilience and the posting Compose subset are implemented; search/client integration remains later-stage work. Future infrastructure must stay inside this application folder. `src/JobBoard.Persistence` remains a placeholder; no shared database implementation was added.

See the [messaging guide](src/JobPosting.Api/docs/messaging.md) for configuration, broker tests and optional local setup. See the [POST workflow guide](src/JobPosting.Api/docs/post-workflow.md) for compensation and failure windows.

See [resilience and shutdown](src/JobPosting.Api/docs/resilience-and-shutdown.md) for circuit settings, readiness, metrics, safe logs and Linux signal evidence.

Docker-only start (from this folder):

```sh
docker compose up --build --detach --wait --wait-timeout 180
```

See [Docker/local development](src/JobPosting.Api/docs/docker-and-local-development.md) for fresh checkout, migration sequencing, host-mode development, POST examples, safe stop and disposable Compose verification.

See the [client integration handoff](docs/client-integration-handoff.md) and [final verification and operational handoff](docs/verification-and-handoff.md). Angular integration, search services, authentication and genuine transcript export remain deferred.
