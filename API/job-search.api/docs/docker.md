# Docker and local development

The search API runs from the **root** [`docker-compose.yml`](../../../docker-compose.yml). That file also starts PostgreSQL, RabbitMQ, both migrations and the posting API. This guide covers only the search API. For the overall quick start, see the [root README](../../../README.md).

## Start

From the repository root:

```sh
docker compose up --build
```

- The search API has its own `job_search` database and `jobsearch` role on the shared PostgreSQL server. An [init script](../../../infra/database/init/01-create-search-database.sql) creates them on first start.
- The API never reads the posting database.
- `job-search-migrate` applies the EF Core migrations once the database is healthy. The API starts only after it succeeds. The API never migrates on its own.

| Endpoint | URL |
|---|---|
| Search API | http://localhost:5101/api/jobs |
| Liveness (no dependency checks) | http://localhost:5101/health/live |
| Readiness (search database and schema) | http://localhost:5101/health/ready |
| Message consumer state | http://localhost:5101/health/ingestion |

If RabbitMQ goes down, only `/health/ingestion` reports it. Searches for jobs already received keep working.

## Image

The [Dockerfile](../Dockerfile) builds from this application folder only. It has two targets: `api` and `migrations` (an EF Core migration bundle).

- Both run as the built-in non-root user, with no SDK inside.
- The SDK, runtime and PostgreSQL images are pinned by tag and digest.
- `curl` is installed for the container health check.

## Cursor signing key

The API signs page tokens with a secret key (see [search.md](search.md#signing-keys)). Outside Development, it needs a key of at least 32 random bytes in base64, shared by every instance.

The root Compose file sets a **local-only** default through `CURSOR_SIGNING_KEY`. For anything shared, set your own value in a root `.env` file. To generate one in PowerShell:

```powershell
[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
```

## Run the API on the host

To debug the API outside Docker, first start its dependencies and migration from the repository root:

```sh
docker compose up -d postgres rabbitmq job-search-migrate
```

Then run from `API/job-search.api` (PowerShell shown; use `export` on macOS or Linux):

```powershell
$env:SearchDatabase__ConnectionString = 'Host=127.0.0.1;Port=5432;Database=job_search;Username=jobsearch'
$env:PGPASSWORD = 'local-development-only-change-me'
$env:RabbitMq__HostName = '127.0.0.1'
$env:RabbitMq__UserName = 'jobboard'
$env:RabbitMq__Password = 'local-development-only-change-me'
dotnet run --project src/JobSearch.Api --launch-profile http
```

- The Development profile listens on http://localhost:5100.
- It serves the OpenAPI document at `/openapi/v1.json`.
- It uses a built-in non-production cursor key if none is set.
- To point the Angular app at it, set `JOB_SEARCH_API_URL=http://localhost:5100`.

If the Docker search API is also running, both consume the same queue and share its messages. Stop one unless you want that.

## Stop and update

```sh
docker compose stop        # stop, keeping containers and data
docker compose down        # remove containers, keep data volumes
docker compose down -v     # also delete the database and broker data
```

- On shutdown, the API has 30 seconds to finish. That includes up to 24 seconds to finish messages in progress. Docker waits 45 seconds.
- Messages not yet acknowledged stay in RabbitMQ and are delivered again.
- After adding a migration, run `docker compose up --build job-search-migrate` before starting the updated API. There is no automatic rollback.

## Test tools

`tools/BrokerFixture` checks a RabbitMQ connection (`--probe`, which changes nothing). Without arguments, it publishes one sample `JobPostingCreated` message to a throwaway exchange and queue, named by `RABBITMQ_EXCHANGE` and `RABBITMQ_QUEUE`. Both names must start with `verify-`, so it never touches the real `job-post-queue`.

References: [Compose startup order](https://docs.docker.com/reference/compose-file/services/), [.NET non-root containers](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-8/containers).
