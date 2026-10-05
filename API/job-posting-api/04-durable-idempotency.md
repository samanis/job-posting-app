# Stage 4: durable idempotency and concurrency

Read `API/job-posting-api/shared-requirements.md` and `requirements-review.md` first. Inspect completed stages and applicable AGENTS.md. Implement only this stage; earlier numbered stages must be complete. Preserve unrelated work.

## Tasks

1. Implement required opaque header parsing, digest/scoping and canonical fingerprint lookup from the agreed contract. A key is caller-provided identity, not business content dedupe or authorization.
2. Implement atomic reserve/create of exactly one job, stable acceptance snapshot and event inside one PostgreSQL transaction. Database unique constraints must resolve races across multiple service hosts; do not use a static dictionary or in-process lock as authority.
3. On unique conflict, resolve the existing record in a clean transaction; equal fingerprint resumes/replays it, different fingerprint returns 409 idempotency_key_conflict. Ensure every concurrent request converges to the same job/event IDs.
4. Resolve a stored matching request before future-date validation. Same-key replay after midnight/closing date must not create a new job or reject previously valid accepted work. Canonicalization version and replay snapshot must remain stable.
5. Define durable publication-progress lookup and bounded in-progress responses for stage 6. Do not return HTTP acceptance yet when no real publisher exists.
6. Implement and test retain-for-job-lifetime behavior; invalid new requests must not permanently consume a key. Unknown database commit outcome requires reread/same-key retry, never replacement identity.

## Acceptance and checks

- Run the mandatory coverage gate: 100% lines, branches and methods for all authored production code, with source completeness and exclusions checked as required by shared-requirements.md. Keep unit/host coverage and external integration evidence accurately distinguished.

- Real PostgreSQL tests race equal and conflicting payloads from independent hosts/DbContexts.
- One equal-key job and one outbox identity exist after concurrency and restart.
- Tests cover canonical numeric equivalence, expired-date replay, conflict safety, atomic rollback and lost/ambiguous commit recovery.
- No fake RabbitMQ acceptance or whole-request automatic retry.

Run appropriate build/tests and record actual results in `API/job-posting-api/ai-log/job-posting-api-work-notes.md`. Report changed files, assumptions and blockers. Do not commit, push or continue into later stages automatically.

