# Docker and local development

The search API runs in Docker through the repository's **root** [`docker-compose.yml`](../../../docker-compose.yml), which also starts PostgreSQL, RabbitMQ, both migrations and the posting API. This guide covers what is specific to the search API. For the overall quick start, see the [root README](../../../README.md).

## Start

From the repository root:

```sh
docker compose up --build
```

The search API uses its own `job_search` database and `jobsearch` role on the shared PostgreSQL server; an [init script](../../../infra/database/init/01-create-search-database.sql) creates them on first start. It never reads the posting database. `job-search-migrate` applies the committed EF Core migrations once the database is healthy, and the API waits for it to exit with code 0; the API never migrates on startup.

| Endpoint | URL |
|---|---|
| Search API | http://localhost:5101/api/jobs |
| Liveness (no dependencies) | http://localhost:5101/health/live |
| Readiness (search database and schema) | http://localhost:5101/health/ready |
| Consumer state | http://localhost:5101/health/ingestion |

A broker outage shows up in `/health/ingestion` only; reads of already-projected jobs keep working.

## Image

The [Dockerfile](../Dockerfile) builds from this application folder only, with two targets: `api` and `migrations` (an EF Core migration bundle). Both run as the built-in non-root user (UID 1654) with no SDK at runtime. The SDK, runtime and PostgreSQL images are pinned by tag and digest. `curl` is installed for the bounded health check.

## Cursor signing key

Production requires an externally supplied cursor signing key: base64 of at least 32 random bytes, shared by every replica. The root Compose file provides a **local-only** default through `CURSOR_SIGNING_KEY`; set your own in a root `.env` for anything shared. To generate one in PowerShell:

```powershell
[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
```

See [search.md](search.md) for key rotation.

## Run the API on the host

To debug the API outside Docker, start its dependencies and migration from the repository root:

```sh
docker compose up -d postgres rabbitmq job-search-migrate
```

Then, from `API/job-search.api` (PowerShell shown; use `export` on macOS/Linux):

```powershell
$env:SearchDatabase__ConnectionString = 'Host=127.0.0.1;Port=5432;Database=job_search;Username=jobsearch'
$env:PGPASSWORD = 'local-development-only-change-me'
$env:RabbitMq__HostName = '127.0.0.1'
$env:RabbitMq__UserName = 'jobboard'
$env:RabbitMq__Password = 'local-development-only-change-me'
dotnet run --project src/JobSearch.Api --launch-profile http
```

The Development profile listens on http://localhost:5100, serves `/openapi/v1.json`, and uses a built-in non-production cursor key if none is configured. Point the Angular app at it with `JOB_SEARCH_API_URL=http://localhost:5100`. Run only one consumer against real messages unless you intend replicas to compete for them.

## Stop and update

```sh
docker compose stop        # stop, keeping containers and data
docker compose down        # remove containers, keep data volumes
docker compose down -v     # also delete the database and broker data
```

Services have a 45-second stop grace; the host allows 30 seconds for shutdown, including a 24-second consumer drain. Unfinished deliveries stay unacknowledged and are redelivered. After adding a migration, rebuild and rerun it with `docker compose up --build job-search-migrate` before starting the updated API. There is no automatic rollback.

## Test tools

`tools/BrokerFixture` can check a broker connection (`--probe`, no changes) or publish one sample `JobPostingCreated` event to a disposable exchange and queue whose names start with `verify-`. It refuses any other topology, so it never touches the real `job-post-queue`.

References: [Compose startup order and dependencies](https://docs.docker.com/reference/compose-file/services/), [.NET non-root containers](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-8/containers).
