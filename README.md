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
- **Caching.** Jobs never change once ingested, so search responses are cached: job details for an hour, and first list pages for up to 15 seconds (never past UTC midnight). Pages with a cursor and errors are never cached. `Cache-Control` headers let browsers and a CDN absorb repeat traffic too. On a synthetic read workload, 99% of requests were served from the cache and median latency fell from 7.2 ms to 0.2 ms.
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

Validation rules: all text fields are required; `salaryMin` must be less than `salaryMax`; `closingDate` must be in the future. The Angular form checks these rules using the browser's date. The API checks them again using today's date in the business time zone (`America/Toronto` by default).

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

- **Unit tests** need no running services. All four projects require 100% coverage. For the APIs, run the coverage check from the API's folder, for example `cd API/job-search.api` then `dotnet run --project tools/CoverageGate`. It fails if any line, branch or method is not covered.
- **API integration tests** run against real PostgreSQL and RabbitMQ containers, which they start and remove themselves. Docker must be running. Run them with `dotnet test API/job-posting-api/tests/JobPosting.Api.IntegrationTests` or `dotnet test API/job-search.api/tests/JobSearch.Api.IntegrationTests`.
- **Browser end-to-end tests** use Playwright with mocked APIs. Install Chromium once, then run the suite:

  ```sh
  npm --prefix apps/job-posting run test:e2e:install
  npm --prefix apps/job-posting run test:e2e
  ```

  The same commands work for `apps/job-search`.

## Running an API outside Docker

To debug an API on your machine, start its database, RabbitMQ and migration in Docker, then run the API with `dotnet run`. The exact commands and environment variables are in each API's Docker guide:

- [Job Posting API](API/job-posting-api/src/JobPosting.Api/docs/docker-and-local-development.md#run-the-api-on-the-host)
- [Job Search API](API/job-search.api/docs/docker.md#run-the-api-on-the-host)

## Project documentation

Each project keeps its detailed notes in a `docs/` folder. The most useful ones:

- **Job Posting API:** [API contract](API/job-posting-api/src/JobPosting.Api/docs/api-contract.md), [idempotency](API/job-posting-api/src/JobPosting.Api/docs/idempotency.md), [POST workflow and compensation](API/job-posting-api/src/JobPosting.Api/docs/post-workflow.md)
- **Job Search API:** [search, paging and caching](API/job-search.api/docs/search.md), [read performance and caching measurements](API/job-search.api/docs/performance/read-performance.md)
- **Job Posting app:** [local development](apps/job-posting/docs/local-development.md), [client-side duplicate protection](apps/job-posting/docs/client-idempotency.md)
- **Job Search app:** [list and detail pages](apps/job-search/docs/list-and-detail-experience.md), [filters and paging in the URL](apps/job-search/docs/url-query-state.md)

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
└── ai-log/                   # AI chat transcripts
```

Each project also has a `prompts/` (or `prompt/`) folder with the numbered prompts used to build it, and a `docs/` folder with detailed notes.

## AI usage

Raw, unedited AI session logs are in [`ai-log/raw/`](ai-log/raw/manifest.json). The manifest records their source files and verification hashes. Each project was built from a sequence of small, reviewed prompts rather than one large request; the prompts are committed alongside each project.

## Final submission

The updated submission instructions require a local Git repository archive or a
Git bundle, rather than a public GitHub submission. Include raw, unedited AI chat
exports from every tool and session in the root `ai-log/` directory. Work notes
and edited transcript presentations do not replace these exports.

Original local Codex and Claude Code JSONL logs are in `ai-log/raw/`, with a hash
manifest. Refresh them with `python ai-log/export-raw-transcripts.py` after the
final AI session, then add and commit all submission files. Check that any sessions
from other tools or machines are also supplied.

To create a bundle containing all local Git refs and their reachable history,
run from the repository root after the final commit:

```sh
git bundle create ../job-posting-app.bundle --all
git bundle verify ../job-posting-app.bundle
```

A bundle includes committed files and history, not uncommitted or untracked files.
The recipient can restore the repository with:

```sh
git clone job-posting-app.bundle job-posting-app
```

Alternatively, zip the local repository with its hidden `.git` directory included;
a source-only archive is insufficient. Send the bundle or ZIP and a separate copy
of `README.md` by replying to the assignment email.

## Commit history

The early history (4–6 October) contains a few large commits with terse messages such as `done` and `version 0`, where whole prompt stages were committed at once. Later commits are small and each describes one change. I've left the early history as it is rather than rewriting it.

## Next steps

Out of scope for this exercise, but the natural next steps:

- A CDN in front of the search API, and Redis as a shared output-cache store once there are several replicas
- Authentication for the posting app and API
- Edit and close operations for postings, which the event-driven read model would need to handle as update events
- CI pipeline running all four test suites and the Compose smoke test
