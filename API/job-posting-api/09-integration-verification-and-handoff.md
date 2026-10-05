# Stage 9: failure verification, documentation and client handoff

Read `API/job-posting-api/shared-requirements.md` and `requirements-review.md` first. Inspect completed stages and applicable AGENTS.md. Implement only this stage; earlier numbered stages must be complete. Preserve unrelated work.

## Tasks

1. Run restore/build and meaningful unit plus PostgreSQL/RabbitMQ integration tests for every authored behavior. Add focused missing cases, not tests that merely duplicate implementation. The PDF requires a unit test; the reliability claims require real dependency/concurrency evidence.
2. Verify request/body/header/decimal/date boundaries; production-safe 400/422/409/503/500; actual full-record 202; original response replay; same key conflicting and equivalent payloads; multi-host races; restart and late-date replay.
3. Verify database rollback/ambiguous commit, broker down/unroutable/nack/timeout, unknown publish result, confirm-before-mark crash, HTTP response loss, worker/request claim races, lease expiry/stale fencing, low-volume circuit recovery and SIGTERM/forced-stop recovery.
4. Verify Compose from a clean source checkout with fresh isolated test volumes, preserving existing user data. Validate startup/migrations, host/container network config, durable restart, docs, ignore rules, lock/tool files and secret redaction.
5. Finish API contract, architecture/reliability notes, runbook and honest work notes. Describe broker acceptance versus search visibility, the at-least-once consumer obligation and exact key retention/fingerprint/timezone limits.
6. Write docs/client-integration-handoff.md: existing posting client treats 202 as unresolved; specify a later scoped change to recognize this API's 202 accepted saved-record contract, show returned record/delayed visibility, and preserve same-key retries for 503 publication_pending. Also document added text/salary limits and business-timezone alignment needed in the client. Do not change either Angular app in this stage.
7. List deferred search API/consumer/query storage/full Compose exercise, auth and genuine transcript export. Do not claim the whole PDF exercise or searchable end-to-end flow is complete.
8. Run the final 100% line/branch/method coverage gate with the complete authored-source set. Keep isolated unit/in-process host categories explicit and real dependency integration results separate. Inspect exclusions, unexecuted-source inclusion and exact counts. Demonstrate threshold and missing-report failures without weakening the committed gate; document its developer/CI command and report locations.

## Acceptance and checks

- All selected tests/build/container checks pass with recorded commands/results; report concrete blockers instead of claiming checks ran.
- Coverage reaches 100% for every required metric and authored file/type, and the gate demonstrably rejects incomplete coverage and missing reports. Integration execution is not misrepresented as isolated unit coverage.
- Fault tests prove one job per retained key and durable recoverable event; duplicates are acknowledged and stable-ID safe.
- Saved-record 202, safe errors, graceful shutdown and circuit behavior agree with docs.
- Only authorized posting/backend-support files changed, all artifacts are reviewable, and no automatic commit/push/deployment occurred.

Run appropriate build/tests and record actual results in `API/job-posting-api/ai-log/job-posting-api-work-notes.md`. Report changed files, assumptions and blockers. Do not commit, push or continue into later stages automatically.

