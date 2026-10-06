# Job Board Mini-App

A monorepo containing four projects that together form a small job board:

| Project | Path | Stack | Purpose |
|---|---|---|---|
| **Job Posting app** | [`apps/job-posting`](apps/job-posting) | Angular 22, Angular Material | Hiring managers create a job posting |
| **Job Posting API** | [`API/job-posting-api`](API/job-posting-api) | .NET 10, EF Core, PostgreSQL | Validates and stores postings, then publishes a `JobPostingCreated` event |
| **Job Search app** | [`apps/job-search`](apps/job-search) | Angular 22, Angular Material | Job seekers browse open positions and view details |
| **Job Search API** | [`API/job-search.api`](API/job-search.api) | .NET 10, EF Core, PostgreSQL | Consumes events into a read model and serves list and detail queries |

## Architecture

Posting is low-volume (a few jobs per day); search is high-volume (many candidates browsing). The write and read sides are therefore separate services with separate databases, connected by a message broker:

```text
 Job Posting app ──POST /api/jobs──▶ Job Posting API ──▶ PostgreSQL: job_postings
   (Angular :4200)                    (.NET :5000)
                                           │ JobPostingCreated (confirmed publish)
                                           ▼
                                       RabbitMQ
                                           │
                                           ▼
 Job Search app ──GET /api/jobs──▶  Job Search API ──▶ PostgreSQL: job_search
   (Angular :4201)                    (.NET :5101)      (read model, trigram indexes)
```

- **Write side.** The posting API validates the request, saves it, and returns `202 Accepted` with the saved record only after the database commit *and* the broker's publish confirmation. An `Idempotency-Key` header makes retries safe, so a double-click or network retry cannot create a duplicate job.
- **Read side.** The search API consumes events idempotently into its own denormalized table. It serves keyset-paginated lists (no `OFFSET` scans) and uses `pg_trgm` GIN indexes for text filters, so read load never touches the write database. Unprocessable messages go to a quarantine queue instead of blocking the consumer.
- **Caching.** Jobs never change once ingested, so search responses are cached: job details for an hour, list pages for 15 seconds, and errors never. `Cache-Control` headers let browsers and a CDN absorb repeat traffic too. On the bundled read workload this cut median latency from 6.9 ms to 0.2 ms.
- **Consistency.** Search is eventually consistent, as the brief allows. A new posting normally reaches the search database within a few seconds; a cached list page can take up to 15 seconds more to show it.
- **Scaling.** Each API can be scaled and deployed independently. The search API is stateless, so it can be scaled out horizontally.

The reasoning behind these choices, the trade-offs accepted, and what a strict 4-hour version would look like are in [DECISIONS.md](DECISIONS.md).

## Quick start

### Prerequisites

- [Docker](https://docs.docker.com/get-docker/) with Docker Compose v2
- [Node.js](https://nodejs.org/) 22.22.3 or later in the 22.x series, with npm
- [.NET SDK](https://dotnet.microsoft.com/download/dotnet/10.0) 10.0.101 or later. Only needed to run the .NET tests or to run an API outside Docker.

### 1. Start the backend

From the repository root:

```sh
docker compose up --build
```

This starts PostgreSQL, RabbitMQ, runs both EF Core migrations, then starts both APIs. No `.env` file is needed; every setting has a local-development default. The first build takes a few minutes.

| Service | URL |
|---|---|
| Job Posting API | http://localhost:5000 |
| Job Search API | http://localhost:5101 |
| RabbitMQ management | http://localhost:15672 (user `jobboard`, password `local-development-only-change-me`) |
| PostgreSQL | `localhost:5432` (databases `job_postings` and `job_search`) |

To change ports or passwords, create a `.env` file in the repository root before the **first** start. The variable names are listed at the top of [docker-compose.yml](docker-compose.yml).

### 2. Start the two Angular apps

In two more terminals:

```sh
npm --prefix apps/job-posting ci
npm --prefix apps/job-posting start
```

```sh
npm --prefix apps/job-search ci
npm --prefix apps/job-search start
```

On Windows PowerShell, use `npm.cmd` if the execution policy blocks `npm`. Each dev server proxies `/api/**` to its API, so there is no CORS setup to do.

### 3. Try it

1. Open **http://localhost:4200** and submit a job. Try invalid values first to see client-side validation, such as a minimum salary above the maximum or a closing date in the past.
2. The confirmation panel shows the record exactly as the API returned it, including its generated `id` and `createdAt`.
3. Open **http://localhost:4201**. The new job appears in the list; click it to see the full details. If the search page was already open, list pages are briefly cached, so wait up to about 15 seconds and refresh.

### Stop

```sh
docker compose down        # keep data
docker compose down -v     # also delete the database and broker volumes
```

## API reference

### Job Posting API

`POST /api/jobs`. Requires an `Idempotency-Key` header containing a UUID.

```sh
curl -i -X POST http://localhost:5000/api/jobs \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: 0f8e2a4c-7b1d-4e3a-9c55-2d6b8f1a3e70" \
  -d '{"title":"Senior Engineer","department":"Engineering","location":"Toronto",
       "description":"Build things.","salaryMin":100000,"salaryMax":130000,"closingDate":"2027-03-31"}'
```

| Status | Meaning |
|---|---|
| `202` | Saved and published. The body is the saved record (`id`, `createdAt` and all submitted fields). Repeating the same key returns the same response. |
| `400` / `422` | Validation failed. The body is a Problem Details object with an `errors` map, which the form shows next to each field. |
| `409` | The idempotency key was reused with a different body, or the original request is still in progress. |
| `503` | The database or broker is unavailable (`Retry-After` is set). |

Validation rules: all text fields are required; `salaryMin` must be less than `salaryMax`; `closingDate` must be after today in the business time zone (`America/Toronto` by default). The same rules are enforced in the Angular form and in the API.

### Job Search API

| Endpoint | Description |
|---|---|
| `GET /api/jobs` | Open jobs (closing date after today). Optional query parameters: `q` (title or description), `department`, `location`, `sort` (`newest` or `closing-soon`), `limit` (1–50, default 20), `cursor`. Returns `{ items, nextCursor }`. |
| `GET /api/jobs/{id}` | Full details of one job, including its description. |

Both APIs expose `GET /health/live` and `GET /health/ready`.

## Running the tests

Each project has its own test suite.

| Project | Command (run from the repository root) |
|---|---|
| Job Posting app | `npm --prefix apps/job-posting run test:coverage` |
| Job Search app | `npm --prefix apps/job-search run test:coverage` |
| Job Posting API | `dotnet test API/job-posting-api/tests/JobPosting.Api.Tests` |
| Job Search API | `dotnet test API/job-search.api/tests/JobSearch.Api.Tests` |

- **Unit tests** need no running services. The Angular suites enforce 100% coverage.
- **API integration tests** (`tests/*.IntegrationTests`) run against real PostgreSQL and RabbitMQ containers, which they start and remove themselves. Docker must be running.
- **Browser end-to-end tests** use Playwright with mocked APIs. Install Chromium once, then run the suite:

  ```sh
  npm --prefix apps/job-posting run test:e2e:install
  npm --prefix apps/job-posting run test:e2e
  ```

  The same commands work for `apps/job-search`.

## Running an API outside Docker

To debug an API on the host, start only the infrastructure with `docker compose up postgres rabbitmq`, then follow that project's README:

- [Job Posting API](API/job-posting-api/README.md)
- [Job Search API](API/job-search.api/README.md)

## Repository layout

```text
.
├── docker-compose.yml        # Whole backend: PostgreSQL, RabbitMQ, migrations, both APIs
├── infra/database/init/      # Creates the search database and role on first start
├── apps/
│   ├── job-posting/          # Angular app 1
│   └── job-search/           # Angular app 2
├── API/
│   ├── job-posting-api/      # .NET API 1: src, unit and integration tests, migrations, docs
│   └── job-search.api/       # .NET API 2: src, unit and integration tests, migrations, docs
└── ai-log/                   # AI chat transcripts and working notes
```

Each project also has a `prompts/` (or `prompt/`) folder with the staged prompts used to build it, and a `docs/` folder with detailed design notes.

## AI usage

All transcripts and AI work notes are in [`ai-log/`](ai-log/README.md). Its README has a guided tour with line numbers for the points where I steered or simplified the AI's design. Each project was built from a sequence of small, reviewed prompts rather than one large request; the prompts are committed alongside each project.

## Commit history

The early history (4–6 October) contains a few large commits with terse messages such as `done` and `version 0`, where whole prompt stages were committed at once. Later commits are small and each describes one change. I've left the early history as it is rather than rewriting it.

## Next steps

Out of scope for this exercise, but the natural next steps:

- A CDN in front of the search API, and Redis as a shared output-cache store once there are several replicas
- Authentication for the posting app and API
- Edit and close operations for postings, which the event-driven read model would need to handle as update events
- CI pipeline running all four test suites and the Compose smoke test
