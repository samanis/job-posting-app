# Docker and local development

The posting API runs in Docker through the repository's **root** [`docker-compose.yml`](../../../../../docker-compose.yml), which also starts PostgreSQL, RabbitMQ, both migrations and the search API. This guide covers what is specific to the posting API. For the overall quick start, see the [root README](../../../../../README.md).

## Start

From the repository root:

```sh
docker compose up --build
```

A successful start shows `postgres`, `rabbitmq` and `job-posting-api` healthy, and `job-posting-migrate` exited with code 0. The migration container runs the committed EF Core migration bundle once PostgreSQL is healthy; the API waits for it to complete and never migrates on startup. If it fails, inspect `docker compose logs job-posting-migrate` rather than skipping the dependency.

| Endpoint | URL |
|---|---|
| Posting API | http://localhost:5000/api/jobs |
| Liveness | http://localhost:5000/health/live |
| Readiness (database and broker) | http://localhost:5000/health/ready |

All published ports bind to 127.0.0.1. The Compose API runs in Production, so the OpenAPI document is not exposed; run the API on the host (below) to get it.

## Image

The [Dockerfile](../../../Dockerfile) has two targets built from this application folder only:

- `api`: the published API on the ASP.NET 10 runtime image.
- `migrations`: an EF Core migration bundle, run once by the `job-posting-migrate` service.

Both run as the built-in non-root user (UID 1654) with exec-form entrypoints and no SDK at runtime. The SDK, runtime, PostgreSQL and RabbitMQ images are pinned by tag and digest; update tags and digests together. `curl` is installed for the bounded health check.

## Run the API on the host

To debug the API outside Docker, start its dependencies and migration from the repository root:

```sh
docker compose up -d postgres rabbitmq job-posting-migrate
```

Then, from `API/job-posting-api` (PowerShell shown; use `export` on macOS/Linux):

```powershell
$env:PostingDatabase__ConnectionString = 'Host=127.0.0.1;Port=5432;Database=job_postings;Username=jobposting'
$env:PGPASSWORD = 'local-development-only-change-me'
$env:RabbitMq__HostName = '127.0.0.1'
$env:RabbitMq__UserName = 'jobboard'
$env:RabbitMq__Password = 'local-development-only-change-me'
dotnet run --project src/JobPosting.Api --launch-profile http
```

The Development profile listens on http://localhost:5000 and serves `/openapi/v1.json`. If the Compose posting API is running, stop it first with `docker compose stop job-posting-api`, since both use port 5000. Adjust the values if you changed them in a root `.env` file.

## POST, replay and conflict

PowerShell:

```powershell
$key = [guid]::NewGuid().ToString('N')
$payload = @{
    title='Engineer'; department='Engineering'; location='Toronto'
    description='Build a small API'; salaryMin=100; salaryMax=200
    closingDate=(Get-Date).AddDays(10).ToString('yyyy-MM-dd')
} | ConvertTo-Json -Compress
Invoke-RestMethod -Method Post -Uri http://localhost:5000/api/jobs -ContentType application/json -Headers @{'Idempotency-Key'=$key} -Body $payload
# Repeat with the SAME key and body: the original saved response; nothing new is inserted or published.
Invoke-RestMethod -Method Post -Uri http://localhost:5000/api/jobs -ContentType application/json -Headers @{'Idempotency-Key'=$key} -Body $payload
# Same key, different title: 409 idempotency_key_conflict.
Invoke-WebRequest -Method Post -Uri http://localhost:5000/api/jobs -ContentType application/json -Headers @{'Idempotency-Key'=$key} -Body ($payload.Replace('Engineer','Other'))
```

curl:

```sh
curl -i http://localhost:5000/api/jobs -H 'Content-Type: application/json' -H 'Idempotency-Key: first-posting-example' \
  --data-binary '{"title":"Engineer","department":"Engineering","location":"Toronto","description":"Build a small API","salaryMin":100,"salaryMax":200,"closingDate":"2028-02-29"}'
```

The key may be 1–128 letters, digits, `_` or `-`. Choose a closing date after today in the business time zone (`America/Toronto` by default). A 202 returns every saved field plus the generated `id` and UTC `createdAt`, only after the database commit and RabbitMQ's publish confirmation; it does not wait for the search API. See the [API contract](api-contract.md) for the 400, 422, 409 and 503 responses.

## Broker outage

From the repository root, stop RabbitMQ, submit a **new** key, then restore it:

```sh
docker compose stop rabbitmq
# POST with a new key: 503 publication_failed once the saved row has been deleted again.
docker compose start rabbitmq
```

This also pauses the search API's consumer, which reconnects on its own. After a publication failure, the saved row is deleted (compensation). An already completed request can still replay its 202 while the broker is down. If cleanup fails or is uncertain, the API returns `publication_unresolved` and logs a Critical event for manual investigation; check `docker compose logs job-posting-api` using the trace, job and event IDs. Logs never contain raw keys, payloads or credentials. The circuit breaker may stay open for up to 15 seconds after the broker returns.

Compensation cannot retract a message RabbitMQ accepted without the confirmation arriving, and a forced termination can leave unresolved rows. There is no outbox or automatic repair; see [POST workflow](post-workflow.md) and the trade-off discussion in the repository's [DECISIONS.md](../../../../../DECISIONS.md).

## Stop

```sh
docker compose stop        # stop, keeping containers and data
docker compose down        # remove containers, keep data volumes
docker compose down -v     # also delete the database and broker data
```

Services have a 45-second stop grace; the API drains in-flight requests for up to 30 seconds.

References: [Compose startup order](https://docs.docker.com/compose/how-tos/startup-order), [EF Core migration bundles](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying), [official .NET images](https://mcr.microsoft.com/en-us/product/dotnet/sdk/about).
