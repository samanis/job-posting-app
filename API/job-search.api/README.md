# Job Search API

.NET 10 Web API behind the [Job Search app](../../apps/job-search). It consumes `JobPostingCreated` events from RabbitMQ into its own PostgreSQL read model and serves the high-volume list and detail queries. It never calls the posting API or reads its database.

For the overall architecture and the one-command Docker setup, see the [root README](../../README.md).

## Endpoints

| Endpoint | Description |
|---|---|
| `GET /api/jobs` | Open jobs (closing date after today, UTC). Optional parameters: `q` (title or description), `department`, `location`, `sort` (`newest` or `closing-soon`), `limit` (1–50, default 20), `cursor`. Returns `{ items, nextCursor }`. List items omit the description. |
| `GET /api/jobs/{id}` | Full details of one job, including the description. Returns 404 if the job is unknown. |
| `GET /health/live`, `/health/ready`, `/health/ingestion` | Liveness, database readiness, and consumer state. |

Full details: [search.md](docs/search.md) and [contracts.md](docs/contracts.md).

## Design for read-heavy load

- **Separate read model.** Jobs are projected into a denormalized `job_search` database, so search traffic never touches the write database.
- **Indexed filtering.** Text filters use `ILIKE` backed by `pg_trgm` GIN indexes. Sorting has matching B-tree indexes.
- **Keyset pagination.** Pages continue from the last row instead of using `OFFSET`, so deep pages cost the same as the first. The cursor is HMAC-signed and pins a snapshot, so new jobs arriving mid-browse don't shift or duplicate results.
- **Idempotent consumer.** Redelivered events are detected and ignored. Malformed or conflicting events go to a quarantine queue instead of blocking the consumer.
- **Response caching.** ASP.NET Core output caching keeps job details for 1 hour and first list pages for up to 15 seconds, never past UTC midnight. Pages with a cursor are never cached, so the cursor is validated on every request; errors and 404s are never cached either. `Cache-Control` headers (`immutable` for details, a matching `max-age` for first pages, `no-store` otherwise) let browsers and CDNs cache too. Cache hits are counted by the `search.cache.hits` metric. See [SearchCaching.cs](src/JobSearch.Api/Search/SearchCaching.cs) and the [cache comparison](docs/performance/README.md#cache-comparison-2026-10-06).
- **Stateless.** Replicas can be scaled out and compete on the same queue; they share only the cursor signing key. Each replica has its own in-memory cache.

## Run

The normal way to run it is the root Compose file:

```sh
docker compose up --build          # from the repository root
```

To run the API on the host (for debugging), start the infrastructure and migrations from the repository root, then run the API from this folder:

```sh
docker compose up -d postgres rabbitmq job-search-migrate
```

```powershell
$env:SearchDatabase__ConnectionString = 'Host=127.0.0.1;Port=5432;Database=job_search;Username=jobsearch'
$env:PGPASSWORD = 'local-development-only-change-me'
$env:RabbitMq__HostName = '127.0.0.1'
$env:RabbitMq__UserName = 'jobboard'
$env:RabbitMq__Password = 'local-development-only-change-me'
dotnet run --project src/JobSearch.Api --launch-profile http
```

The development profile listens on http://localhost:5100 and serves OpenAPI at `/openapi/v1.json`. In Development a built-in, non-production cursor signing key is used, and Production refuses to start with it. To point the Angular app at this port, set `JOB_SEARCH_API_URL=http://localhost:5100` before `npm start`.

## Test

Run from this folder:

```sh
dotnet test tests/JobSearch.Api.Tests                 # unit and in-process host tests; no services needed
dotnet test tests/JobSearch.Api.IntegrationTests      # real PostgreSQL and RabbitMQ; needs Docker running
dotnet run --project tools/CoverageGate               # fails unless unit and host coverage are 100%
```

The integration tests start and remove their own containers. `tools/ReadWorkload` measures list and detail latency and captures PostgreSQL query plans; see [performance](docs/performance/README.md).

## Project layout

```text
src/JobSearch.Api/
  Search/          list and detail endpoints, read store, signed cursors
  Messaging/       RabbitMQ consumer, event handler, quarantine publisher
  Persistence/     EF Core DbContext, projection store and migrations
  Contracts/       event and query parsing and validation
  Diagnostics/     health endpoints, structured logs, metrics, safe errors
tests/
  JobSearch.Api.Tests/               unit and host tests
  JobSearch.Api.IntegrationTests/    PostgreSQL and RabbitMQ tests
tools/
  CoverageGate/    coverage enforcement
  ReadWorkload/    read-performance measurement
  BrokerFixture/   publishes test events to a broker
```

## Further reading

- [Design](docs/design.md) and [runbook](docs/runbook.md)
- [Persistence and migrations](docs/persistence.md)
- [Consumer lifecycle and quarantine](docs/messaging.md)
- [Search, cursors and signing-key rotation](docs/search.md)
- [Implementation prompts](prompt/README.md) used to build this API with AI
