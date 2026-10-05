# Docker and local development (Stage 8)

All backend build, infrastructure and configuration inputs are under `API/job-posting-api`. This Compose subset starts only the posting API, its PostgreSQL database, RabbitMQ and an administrative one-shot migration container. It contains no search API/consumer/query database or frontend. Client integration remains a later task.

## Start from a fresh checkout

For the container route, install Docker with Linux containers and Docker Compose v2. Initial image/package downloads require internet access; no host .NET SDK, global EF tool, local database or untracked environment file is needed.

From the repository root:

```sh
cd API/job-posting-api
docker compose config --quiet
docker compose up --build --detach --wait --wait-timeout 180
docker compose ps --all
```

`docker compose up --build` also works in the foreground. A successful initial start shows `postgres`, `rabbitmq` and `job-posting-api` healthy and `migrate` exited with code 0. The migration service runs the committed EF migration bundle once after PostgreSQL is healthy. The API depends on its successful completion and broker health; application replicas never auto-migrate. A failed migration blocks API creation/readiness. Inspect `docker compose logs migrate` if it fails; do not skip the migration dependency to hide a failure.

| Local endpoint | Default |
| --- | --- |
| Posting API | http://localhost:5000/api/jobs |
| Liveness | http://localhost:5000/health/live |
| Readiness | http://localhost:5000/health/ready |
| PostgreSQL | localhost:5432, database job_postings |
| RabbitMQ AMQP | localhost:5672 |
| RabbitMQ management | http://localhost:15672 |

All published ports bind to 127.0.0.1. PostgreSQL and RabbitMQ use the public LOCAL DEVELOPMENT sample username `jobposting` and password `local-development-only-change-me`. These are deliberately committed examples, not production secrets. The Compose API runs in Production, so no development OpenAPI page is exposed. Named volumes persist database/queue data. The broker's fixed `rabbitmq` hostname keeps its Erlang node identity stable across container recreation.

## Configuration

Compose contains the safe sample defaults, so `.env` is optional. To change ports or initial local credentials, copy `.env.example` to the ignored `.env` before first initialization:

```powershell
Copy-Item .env.example .env
```

```sh
cp .env.example .env
```

Edit `JOB_POSTING_API_PORT`, `POSTGRES_PORT`, `RABBITMQ_PORT`, `RABBITMQ_MANAGEMENT_PORT` if their ports are occupied. Change sample credentials for your own local setup as needed. Files containing real secrets are ignored by Git and excluded from the Docker build context. Changing init credentials in `.env` does not rotate users already stored in volumes; keep the matching existing credentials or explicitly rotate the stored users through their administrative tools. Do not delete volumes to resolve a password mismatch.

Within containers, the database hostname is `postgres:5432` and the broker is `rabbitmq:5672`, independent of published host ports. The database password is supplied as `PGPASSWORD`, not concatenated into the connection string. Host-mode uses `127.0.0.1`/`localhost` and the published ports. All API settings still support normal ASP.NET environment overrides; Compose maps the chosen timezone from `JOB_POSTING_TIME_ZONE`. See [resilience](resilience-and-shutdown.md) for validated circuit/publish bounds.

The Dockerfile pins verified .NET SDK 10.0.101 and ASP.NET runtime 10.0.1 to immutable digests, matching the repository's tested baseline; PostgreSQL 18.6 and RabbitMQ 4.3.6-management are also digest-pinned. These are tested compatible versions, not a claim of latest servicing. Update tags and digests together with the SDK/package baseline and rerun verification. The image build supports the amd64/arm64 RID mapping; the verification recorded here ran Linux amd64.

The build restores locked production dependencies and the committed dotnet-ef 10.0.4 manifest, publishes the API, and generates a framework-dependent EF bundle. Separate `api` and `migrations` runtime targets run as the built-in non-root user (UID 1654), with exec-form entrypoints and no SDK/tool restore at runtime. curl supplies bounded readiness health checks; its Ubuntu packages are fetched from the base image's repositories during build. The allowlist `.dockerignore` admits SDK/NuGet/build manifests, tool manifest and production source/migrations/locks, while excluding frontend files, tests, bin/obj/results and secrets. Build context is this application folder, never the repository root.

## Explicit migrations and safe restart

Normal start sequences migrations automatically through the one-shot service. To run the administrative step explicitly:

```sh
docker compose up --detach --wait postgres
docker compose build migrate
docker compose run --rm --no-deps migrate
```

The bundle records applied migration history; rerunning against the same database does not create a second job table or change saved jobs. Before upgrading an existing database, back it up and review the committed migration SQL/history. The data-preserving job-row upgrade is forward-only and aborts incomplete old metadata atomically; see [persistence](persistence.md). A different Compose project name owns different containers/volumes by default; keep the same project name to resume your data.

Normal stop/resume:

```sh
docker compose stop
docker compose up --detach --wait --wait-timeout 180
```

Stop grace is 45 seconds; the application host drains accepted work for 30 seconds, including independently bounded cleanup. `docker compose down` removes containers/network while retaining named volumes, if container recreation is intended. Never use `down -v` for routine stopping. Broker/default-user configuration is initialization-only. Readiness/restarts do not repair unpublished business rows.

## Host .NET development

Install the stable SDK selected by `global.json` (10.0.101 or a compatible 10.0.1xx patch), then run from this application folder:

```sh
dotnet restore JobBoard.slnx --locked-mode
dotnet tool restore
dotnet build JobBoard.slnx --configuration Release --no-restore
dotnet run --project tools/CoverageGate
```

The coverage gate runs isolated unit and separate bootstrap host suites; neither needs a running database/broker. The full integration suite independently creates its own disposable Docker resources:

```sh
dotnet test tests/JobPosting.Api.IntegrationTests --configuration Release --no-restore
```

To develop the API outside Docker, start only its dependencies and migrate:

```sh
docker compose up --detach --wait postgres rabbitmq
docker compose build migrate
docker compose run --rm --no-deps migrate
```

If the Compose API already occupies port 5000, run `docker compose stop job-posting-api` before the host API. Configure host credentials to match the initialized volumes. PowerShell, using the sample values:

```powershell
$env:PostingDatabase__ConnectionString = 'Host=127.0.0.1;Port=5432;Database=job_postings;Username=jobposting'
$env:PGPASSWORD = 'local-development-only-change-me'
$env:RabbitMq__HostName = '127.0.0.1'
$env:RabbitMq__Port = '5672'
$env:RabbitMq__UserName = 'jobposting'
$env:RabbitMq__Password = 'local-development-only-change-me'
dotnet run --project src/JobPosting.Api --launch-profile http
```

macOS/Linux:

```sh
export PostingDatabase__ConnectionString='Host=127.0.0.1;Port=5432;Database=job_postings;Username=jobposting'
export PGPASSWORD='local-development-only-change-me'
export RabbitMq__HostName=127.0.0.1 RabbitMq__Port=5672
export RabbitMq__UserName=jobposting RabbitMq__Password='local-development-only-change-me'
dotnet run --project src/JobPosting.Api --launch-profile http
```

Match overrides to your actual host ports/credentials. The launch profile serves http://localhost:5000 in Development with `/openapi/v1.json`. Ctrl+C gracefully stops only the host API; dependency volumes persist. Alternatively, run `dotnet ef database update --project src/JobPosting.Api` after tool restore with the host connection environment set, instead of running the bundle. Do not run multiple administrative migration commands concurrently with application replicas.

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
# Repeat with the SAME key/body: original saved response; no new insert or publish.
Invoke-RestMethod -Method Post -Uri http://localhost:5000/api/jobs -ContentType application/json -Headers @{'Idempotency-Key'=$key} -Body $payload
# Same key, different title: 409 idempotency_key_conflict.
Invoke-WebRequest -Method Post -Uri http://localhost:5000/api/jobs -ContentType application/json -Headers @{'Idempotency-Key'=$key} -Body ($payload.Replace('Engineer','Other'))
```

PowerShell treats non-success HTTP statuses as errors; inspect its status/Problem Details. For curl on macOS/Linux (or `curl.exe` with a UTF-8 `--data-binary @file.json` payload on Windows):

```sh
curl -i http://localhost:5000/api/jobs -H 'Content-Type: application/json' -H 'Idempotency-Key: first-posting-example' --data-binary '{"title":"Engineer","department":"Engineering","location":"Toronto","description":"Build a small API","salaryMin":100,"salaryMax":200,"closingDate":"2028-02-29"}'
```

Choose a closing date later than today in the configured business timezone. A 202 returns all saved fields, stable UUID/UTC createdAt, status/message, after commit, confirmed/routable broker acceptance and persisted PublishedAt. It does not promise search visibility. The first request requires a UI-generated key; never replace the key on an unresolved retained attempt. See [API contract](api-contract.md) for 400/422/409/503 errors.

## Broker outage and investigation

In your own local development stack, stop RabbitMQ, submit a NEW key/payload, then restore it:

```sh
docker compose stop rabbitmq
# POST a new key: 503 publication_failed if exact-job compensation succeeds.
docker compose start rabbitmq
```

The created row is deleted after a publication exception, including an open circuit; a matching already-completed request can still replay 202 while the broker is offline. A failed/uncertain cleanup or confirmed-before-status write failure returns `publication_unresolved`, emits the corresponding safe Critical event and requires manual investigation. Inspect `docker compose logs job-posting-api` using its trace/job/event IDs; logs suppress raw keys, payloads and credentials. Broker management shows queue activity but no consumer/search processing is implemented. The breaker may remain open for its configured 15 seconds after the broker returns; readiness probes can then restore capability without republishing any rows.

Compensation cannot retract a message accepted without an observed acknowledgement. A later recreation after deletion can duplicate downstream delivery; forced termination can leave unresolved records with no Critical log. Restart/readiness does not repair those records. There is no outbox, scanner, recovery endpoint or automatic message/cleanup retry. See [workflow](post-workflow.md).

## Disposable Compose verification

From this application folder, Windows PowerShell 5.1 or PowerShell 7:

```powershell
./tools/Verify-Compose.ps1
```

The script always generates a new project name, uses dynamically assigned loopback ports, checks that it owns no preexisting volumes, and uses only committed sample config. It verifies empty-volume migrations/non-root/no-SDK runtime, POST/byte-for-byte replay/conflict/one queued event, retained metadata/messages after restart, broker outage compensation, graceful API exit, explicit migration rerun, and container recreation against retained named volumes. It then removes ONLY its newly owned verification containers/network/volumes/image tags. That disposable cleanup is intentionally different from the normal development stop path and never targets `job-posting-api` volumes. Build cache and downloaded base images may remain. On macOS/Linux, run the optional script with PowerShell 7; Docker startup itself needs no PowerShell.

References: [Compose startup sequencing](https://docs.docker.com/compose/how-tos/startup-order), [EF migration bundles](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying), [official .NET images](https://mcr.microsoft.com/en-us/product/dotnet/sdk/about).
