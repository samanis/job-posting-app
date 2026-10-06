# Design decisions

The key decisions behind this solution, the trade-offs I accepted, and what I would change. For setup and architecture, see the [README](README.md).

## 1. Separate write and read services, connected by RabbitMQ

**Decision.** Each API owns its own PostgreSQL database. The posting API publishes a `JobPostingCreated` event to RabbitMQ, and the search API consumes it into a denormalized read model.

**Why.** The brief describes a strong load imbalance: a handful of posts per day against continuous browsing. Separating the two sides means:

- search traffic never competes with writes for the same database;
- each side can be scaled, deployed and tuned on its own (the read side gets trigram indexes and keyset pagination; the write side stays normalized);
- the only contract between the services is a versioned event, so neither depends on the other's schema or availability.

**Alternative considered.** My AI assistant's first proposal was simpler: one shared database, written by the posting API and read by the search API. That fits a 4-hour box well, and at this volume it would perform fine. I chose the broker because it is the design I would defend in production, and because "eventually consistent is acceptable" in the brief points towards asynchronous propagation. The publisher sits behind an `IJobEventPublisher` abstraction, so the broker can be swapped without touching the workflow.

## 2. Return 202 only after the database commit *and* the broker's confirmation

**Decision.** `POST /api/jobs` saves the job, publishes the event with RabbitMQ publisher confirms, and returns `202` with the saved record only when both have succeeded. If publication fails, the API deletes the job it just saved (compensation) and returns `503`, so the caller can retry with the same idempotency key.

**Why.** A `202` should mean "this job *will* appear in search". The alternative I was offered, a transactional outbox, would return success once the job and the event are committed together, and publish in the background.

**Trade-off I accepted.** Compensation is not an atomic rollback across PostgreSQL and RabbitMQ. There are narrow failure windows:

- the broker accepts a message but the confirmation is lost, so the job is deleted while its event is still queued;
- the process crashes between commit and publish, leaving a saved job whose event was never sent.

These need manual investigation; they are logged and measured, but not repaired automatically. Posting is also unavailable while the broker is down. At a few posts per day that is acceptable, but **in production I would switch to a transactional outbox**: it closes both windows and keeps posting available during broker outages.

## 3. Idempotency on both client and server

**Decision.** The Angular app generates an `Idempotency-Key` per submission and keeps it across retries. The API stores the key and a fingerprint of the request alongside the job, in the same transaction.

**Why.** Double-clicks, timeouts and network retries are the most common way a create-only form produces duplicates. Retrying with the same key returns the original response. Reusing a key with a different body returns `409`.

## 4. Read side built for volume

- **Keyset pagination** instead of `OFFSET`, so deep pages cost the same as the first.
- **`pg_trgm` GIN indexes** for the title, description, department and location filters, plus B-tree indexes matching both sort orders.
- **List responses omit the description**; it is only returned by the detail endpoint.
- **Idempotent consumer**: redelivered events are ignored, and malformed or conflicting events go to a quarantine queue instead of blocking the queue.
- **Stateless API**, so replicas can be added behind a load balancer and compete on the same queue.

Pagination cursors are HMAC-signed and pin a snapshot (the highest ingested sequence number and the UTC date when browsing started), so every page of one browsing session sees the same set of jobs. This is more than the exercise needs. Plain `(createdAt, id)` keyset paging already delivers almost all of the benefit: it never shows a job twice when new jobs are inserted, and costs the same at any depth. The snapshot only adds that new jobs don't appear in later pages mid-browse, and that a job doesn't disappear at midnight mid-browse; the signature only adds tamper-proofing to a query that is already fully parameterized. In a real product I would start with the plain version and add the snapshot only if users reported a problem. I kept it here because it is correct and tested, and because a pinned snapshot makes every page after the first safe to cache. See also section 6.

## 5. Testing

- **Unit tests** in all four projects, with a 100% coverage gate. This was a deliberate stretch goal: it forced every error path (4xx, 5xx, timeouts, retries) to be specified and tested, not just the happy path.
- **Integration tests** for both APIs against real PostgreSQL and RabbitMQ containers. They are kept separate, so they can't inflate the unit coverage numbers.
- **Playwright end-to-end tests** for both Angular apps, with mocked APIs.

## 6. Scope: I went well past the 4-hour box

I'm aware this solution took much longer than four hours. I treated the exercise as a production feature, to show how I would reason about failure modes, consistency and load rather than only the happy path. The cost is a solution that is larger than the problem, and more for a reviewer to read.

**What a 4-hour version would look like:**

| Keep | Cut or simplify |
|---|---|
| Two Angular apps with client and server validation | 100% coverage gates; keep focused tests on validation and the API contract |
| Two APIs, EF Core migrations, root Docker Compose | Signed snapshot cursors; use simple keyset or offset paging |
| RabbitMQ event from posting to search (or one shared database) | Circuit breaker, graceful-shutdown harness, quarantine queue |
| Idempotency key on POST | Read-performance tooling and per-stage documentation |

## 7. Known gaps and next steps

- **HTTP caching on search.** No `Cache-Control` headers or output caching yet. List and detail responses are good candidates for short-lived caching, and a CDN in front of the search API. This is the next change I would make for read volume.
- **Transactional outbox** in the posting API (see section 2).
- **One definition of "open".** Posting validates the closing date in the business time zone (`America/Toronto`), while search stops listing a job once its closing date arrives in UTC. Near midnight the two can disagree, and a job disappears from search on its closing day rather than after it. They should share one rule.
- **Authentication and authorization** for the posting side. Out of scope here.
- **Edit and close** operations, which the read model would need to handle as update events.
- **CI pipeline** running all four test suites and a Compose smoke test.

## 8. How I used AI

I used AI as a pair programmer within a staged process: a requirements review first, then a sequence of small numbered prompts per project, each built, tested and reviewed before moving on. I made the architectural calls myself, and the transcripts show where I overrode suggestions: the broker over a shared database, and the strict 202 rule over the outbox. Prompts are committed in each project, and transcripts are in [`ai-log/`](ai-log).
