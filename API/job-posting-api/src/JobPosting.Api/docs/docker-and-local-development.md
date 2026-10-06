# Docker and local development

The root [`docker-compose.yml`](../../../../../docker-compose.yml) starts the whole backend: PostgreSQL, RabbitMQ, both database migrations, the posting API and the search API. This page covers what is specific to the posting API. For the overall quick start, see the [root README](../../../../../README.md).

## Start

From the repository root:

```sh
docker compose up --build
```

No `.env` file is needed. Every setting has a local development default.

The start has succeeded when `postgres`, `rabbitmq` and `job-posting-api` are healthy and `job-posting-migrate` has exited with code 0. The migration container applies the database schema once PostgreSQL is ready. The API waits for it and never changes the schema itself. If the migration fails, check `docker compose logs job-posting-migrate`.

| Endpoint | URL |
|---|---|
| Posting API | http://localhost:5000/api/jobs |
| Liveness (the process is running) | http://localhost:5000/health/live |
| Readiness (database and RabbitMQ are reachable) | http://localhost:5000/health/ready |
| RabbitMQ management UI | http://localhost:15672 |

All ports listen on 127.0.0.1 only. The API in Docker runs in Production mode, so it does not serve the OpenAPI document. To get it, run the API on the host (below).

## Image

The [Dockerfile](../../../Dockerfile) builds two images:

- `api`: the published API on the ASP.NET 10 runtime image.
- `migrations`: an EF Core migration bundle, a single program that applies the database schema. The `job-posting-migrate` service runs it once.

Both run as a non-root user and contain no .NET SDK. All base images are pinned by tag and digest. Docker checks the API's health by calling `/health/ready`.

## Run the API on the host

To debug the API outside Docker, start its dependencies from the repository root:

```sh
docker compose up -d postgres rabbitmq job-posting-migrate
```

Then run the API from `API/job-posting-api`. The example uses PowerShell. On macOS or Linux, use `export` instead.

```powershell
$env:PostingDatabase__ConnectionString = 'Host=127.0.0.1;Port=5432;Database=job_postings;Username=jobposting'
$env:PGPASSWORD = 'local-development-only-change-me'
$env:RabbitMq__HostName = '127.0.0.1'
$env:RabbitMq__UserName = 'jobboard'
$env:RabbitMq__Password = 'local-development-only-change-me'
dotnet run --project src/JobPosting.Api --launch-profile http
```

The API runs in Development mode on http://localhost:5000 and serves `/openapi/v1.json`. The Docker API uses the same port. Stop it first with `docker compose stop job-posting-api`. If you changed any values in a root `.env` file, use those values here.

## Try it: post, replay and conflict

PowerShell:

```powershell
$key = [guid]::NewGuid().ToString('N')
$payload = @{
    title='Engineer'; department='Engineering'; location='Toronto'
    description='Build a small API'; salaryMin=100; salaryMax=200
    closingDate=(Get-Date).AddDays(10).ToString('yyyy-MM-dd')
} | ConvertTo-Json -Compress
Invoke-RestMethod -Method Post -Uri http://localhost:5000/api/jobs -ContentType application/json -Headers @{'Idempotency-Key'=$key} -Body $payload
# Same key and body: returns the original response. Nothing new is saved or published.
Invoke-RestMethod -Method Post -Uri http://localhost:5000/api/jobs -ContentType application/json -Headers @{'Idempotency-Key'=$key} -Body $payload
# Same key, different title: 409 idempotency_key_conflict.
Invoke-WebRequest -Method Post -Uri http://localhost:5000/api/jobs -ContentType application/json -Headers @{'Idempotency-Key'=$key} -Body ($payload.Replace('Engineer','Other'))
```

curl:

```sh
curl -i http://localhost:5000/api/jobs -H 'Content-Type: application/json' -H 'Idempotency-Key: first-posting-example' \
  --data-binary '{"title":"Engineer","department":"Engineering","location":"Toronto","description":"Build a small API","salaryMin":100,"salaryMax":200,"closingDate":"2028-02-29"}'
```

The closing date must be after today in the business time zone (`America/Toronto` by default). A `202` returns the saved job with its generated `id` and `createdAt`. The [API contract](api-contract.md) lists every status code.

## Try it: RabbitMQ outage

From the repository root, stop RabbitMQ, post a job with a **new** key, then start RabbitMQ again:

```sh
docker compose stop rabbitmq
# POST with a new key: 503 publication_failed. The saved job has been deleted again.
docker compose start rabbitmq
```

A request that had already succeeded still replays its `202` while RabbitMQ is down. After RabbitMQ returns, new posts may still fail for up to 15 seconds while the circuit breaker stays open. The [POST workflow](post-workflow.md) explains the circuit breaker and the failure cases.

To investigate a failure, run `docker compose logs job-posting-api` and search for the trace ID from the error response. Logs never contain raw keys, request bodies or passwords.

## Stop

```sh
docker compose stop        # stop, keeping containers and data
docker compose down        # remove containers, keep data volumes
docker compose down -v     # also delete the database and RabbitMQ data
```

Docker gives each service 45 seconds to stop. The API finishes requests already in progress for up to 30 seconds.
