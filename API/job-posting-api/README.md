# Job Posting API

.NET 10 Web API behind the [Job Posting app](../../apps/job-posting). It validates new job postings, stores them in PostgreSQL through EF Core, and publishes a `JobPostingCreated` event to RabbitMQ for the [Job Search API](../job-search.api).

For the overall architecture and the one-command Docker setup, see the [root README](../../README.md).

## Endpoint

`POST /api/jobs` with a JSON body and an `Idempotency-Key` header containing a UUID.

- **202** — saved *and* the event confirmed by the broker. The body is the saved record.
- **400 / 422** — validation errors as Problem Details with an `errors` map.
- **409** — the key was reused with a different body, or the first request is still in progress.
- **503** — database or broker unavailable; `Retry-After` is set.

Health: `GET /health/live`, `GET /health/ready`. Full contract: [api-contract.md](src/JobPosting.Api/docs/api-contract.md).

## How a request is processed

1. The body is parsed strictly and validated: required fields, `salaryMin < salaryMax`, and `closingDate` after today in the business time zone (`America/Toronto` by default).
2. The job and its idempotency metadata are committed in one transaction. A repeated key with the same body replays the original response instead of creating a duplicate.
3. The event is published with RabbitMQ publisher confirms. The API returns 202 only after the broker confirms. If publication fails, the saved row is compensated so callers never see a success that search will not receive.
4. A circuit breaker stops calling the broker while it is failing, and shutdown drains in-flight requests.

## Run

The normal way to run it is the root Compose file, which also starts PostgreSQL, RabbitMQ and the migrations:

```sh
docker compose up --build          # from the repository root
```

To run the API on the host (for debugging), start the infrastructure and migrations from the repository root, then run the API from this folder:

```sh
docker compose up -d postgres rabbitmq job-posting-migrate
```

```powershell
$env:PostingDatabase__ConnectionString = 'Host=127.0.0.1;Port=5432;Database=job_postings;Username=jobposting'
$env:PGPASSWORD = 'local-development-only-change-me'
$env:RabbitMq__HostName = '127.0.0.1'
$env:RabbitMq__UserName = 'jobboard'
$env:RabbitMq__Password = 'local-development-only-change-me'
dotnet run --project src/JobPosting.Api --launch-profile http
```

The development profile listens on http://localhost:5000, the same port the Angular proxy uses, and serves OpenAPI at `/openapi/v1.json`.

## Test

Run from this folder:

```sh
dotnet test tests/JobPosting.Api.Tests                # unit and in-process host tests; no services needed
dotnet test tests/JobPosting.Api.IntegrationTests     # real PostgreSQL and RabbitMQ; needs Docker running
dotnet run --project tools/CoverageGate               # fails unless unit and host coverage are 100%
```

The integration tests start and remove their own containers.

## Project layout

```text
src/JobPosting.Api/
  Posting/         POST controller and save → publish → compensate workflow
  Validation/      strict request parsing, validation rules, request fingerprint
  Idempotency/     duplicate detection and response replay
  Persistence/     EF Core DbContext, store and migrations
  Messaging/       JobPostingCreated event and RabbitMQ publisher
  Resilience/      circuit breaker, health endpoints, graceful shutdown
  Diagnostics/     structured logs, metrics, safe error responses
  docs/            detailed design notes for each area
tests/
  JobPosting.Api.Tests/              unit and host tests
  JobPosting.Api.IntegrationTests/   PostgreSQL and RabbitMQ tests
  JobPosting.ShutdownHarness/        process used by the shutdown-signal tests
tools/CoverageGate/                  coverage enforcement
```

## Further reading

- [Persistence and migrations](src/JobPosting.Api/docs/persistence.md)
- [Idempotency](src/JobPosting.Api/docs/idempotency.md)
- [Messaging](src/JobPosting.Api/docs/messaging.md) and [POST workflow](src/JobPosting.Api/docs/post-workflow.md)
- [Resilience and shutdown](src/JobPosting.Api/docs/resilience-and-shutdown.md)
- [Service README](src/JobPosting.Api/README.md): configuration, logging and coverage-gate details
- [Implementation prompts](prompts/README.md) used to build this API with AI
