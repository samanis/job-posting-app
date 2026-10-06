# AI log

Transcripts of the AI-assisted sessions that built this repository, in order, plus the work notes the AI kept per project.

## How I worked with AI

- **Requirements first.** Each project started with the AI reviewing the brief and my design constraints, then proposing a set of small, numbered prompts. I reviewed the prompt list before anything was built.
- **One prompt at a time.** Each prompt was run, built and tested (with a 100% coverage gate) before the next. The prompts are committed in each project's `prompts/` folder.
- **I made the architecture calls,** and often overrode or simplified the AI's suggestions. The guided tour below points to those moments.

## Transcripts

| # | File | Dates | Covers |
|---|---|---|---|
| 1 | [01-analysis-and-client-apps.md](01-analysis-and-client-apps.md) | 4 Oct 2026 | Analysis of the brief, solution layout, and building both Angular apps prompt by prompt. 260 KB. |
| 2 | [02-client-polish-posting-api-search-api.md](02-client-polish-posting-api-search-api.md) | 4–6 Oct 2026 | Angular Material and Playwright for the clients, then the whole Job Posting API, then the whole Job Search API. 1 MB, because it includes every command and file the AI wrote. |
| 3 | [03-review-compose-docs-caching.md](03-review-compose-docs-caching.md) | 6 Oct 2026 | Claude Code session: a hiring-manager-style review of the submission, then the root `docker-compose.yml`, README rewrites, `DECISIONS.md`, search caching, code cleanup and a clean-clone test. Tool output is shortened. |

The transcripts are unedited exports, apart from the third, where an email address is redacted. They are long because they include every command the AI ran. The line numbers below lead straight to the conversations that matter.

## Guided tour: where I steered the design

**Transcript 1: analysis and client apps**

| Line | What happens |
|---|---|
| 3–17 | I ask for an analysis of the brief. The AI's first proposal is one shared database with no message broker. |
| 37–44 | My up-front constraints: Angular 22 features, idempotency on client *and* server, 100% unit coverage, explicit handling of 2xx/4xx/5xx responses. |
| 215 | I question whether a single-page app needs a lazy route. |
| 667 | I flag hard-coded values in the generated code. |
| 3673–3700 | I ask why an error appears on first load, and whether that behaviour was even in the requirements. |

**Transcript 2: posting API and search API**

| Line | What happens |
|---|---|
| 331–345 | My requirements for the posting API: its own PostgreSQL database, RabbitMQ behind an abstraction, a stateless API, idempotency, a circuit breaker, graceful shutdown. |
| 367–395 | The AI recommends a transactional outbox. I choose to return 202 only after the database commit *and* RabbitMQ's publish confirmation. |
| 1280–1340 | I ask for a data-flow diagram, then say **"I think we are over engineering the whole job"** and ask why a separate idempotency ledger is needed. |
| 1357–1440 | We remove the ledger: the idempotency key moves onto the job row, and concurrency is reduced to a unique constraint. |
| 1502–1545 | I question why jobs are persisted at all, and specify compensation: if publishing fails, delete the saved row and log a fatal error. |
| 1583–1600 | I limit the API's responsibility: once RabbitMQ accepts the message, the posting API is done, with no dependency on the consumer. |
| 1725–1745 | The prompts are rewritten for the simplified design before the remaining stages run. |
| 5960 | Requirements for the search API: an independent codebase with its own database, consuming from the existing broker. |
| 7426–7446 | I correct the AI's wording to insist on a separate PostgreSQL database for search, not just separate storage. |
| 10094–10118 | I ask how the search API receives and processes queued messages before the final stage. |

**Transcript 3: review and hardening**

- The AI reviews the repository as a hiring manager would, and finds the missing root `docker-compose.yml`, stale READMEs and the scope overrun.
- I propose loading every job into Redis at startup and updating it alongside the database. The AI shows, using the repository's own performance measurements, why that wouldn't make search much faster. We implement HTTP and output caching instead, then measure the result.
- I ask whether to replace the signed pagination cursors with plain `(createdAt, id)` keyset paging. We keep them, and document why in `DECISIONS.md`.
- The dense search API code is split to one statement per line by three subagents working in parallel. Their changes are accepted only after a verifier proves them whitespace-only, and after the coverage gate passes.
- The finished branch is cloned fresh from GitHub and run exactly as the README describes, ending with a real browser test that posts a job and finds it in search.

## Work notes

The AI kept these notes while running each prompt: what it changed, which checks it ran, and the results. They are summaries, not transcripts.

| File | Project |
|---|---|
| [work-notes/job-posting-client.md](work-notes/job-posting-client.md) | Job Posting app |
| [work-notes/job-search-client.md](work-notes/job-search-client.md) | Job Search app |
| [work-notes/job-posting-api.md](work-notes/job-posting-api.md) | Job Posting API |
| [work-notes/job-search-api.md](work-notes/job-search-api.md) | Job Search API |

Older prompts and notes refer to these files by their previous locations (`ai-log/*-work-notes.md` and `API/*/ai-log/`). They were moved here so that all AI records are in one folder.
