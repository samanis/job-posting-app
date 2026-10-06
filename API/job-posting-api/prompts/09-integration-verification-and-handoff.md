# Stage 9: verification, documentation and client handoff

Read `API/job-posting-api/prompts/shared-requirements.md` and `requirements-review.md` first. Inspect completed stages and applicable AGENTS.md. Implement only this stage; preserve unrelated work. Revised Stages 3-8 already implement job-row idempotency, direct publication/compensation and the Docker posting subset. Verify that baseline; do not reintroduce the superseded ledger/outbox design.

## Tasks

1. Run locked restore/build and meaningful unit/host plus real PostgreSQL/RabbitMQ integration checks. Extend behavior tests only where required and report actual outcomes.
2. Verify strict body/header/decimal/date boundaries, safe 400/422/409/503/500 and full-record 202; equal/conflicting payloads, independent-host races, completed replay after expiry and no duplicate publication on known completed requests.
3. Verify insert rollback/uncertain commit, broker down/unroutable/nack/timeout/lost acknowledgment, compensation success/failure/timeout, Critical logs, confirm-before-status-write failure, response loss, circuit recovery and graceful/forced stop. There is no dispatcher/lease recovery test or no-loss promise.
4. Validate fresh-checkout Compose with isolated test volumes, explicit migrations, network settings, durable job/broker restart, ignored secrets and all tracked build/run inputs. Preserve user data.
5. Finish current API contract/design/runbook/work notes. State job-row idempotency, no ledger/outbox, direct publication, one attempt, bounded deletion, manual investigation of unresolved records and acceptance of duplicate/lost-delivery/crash gaps. Do not implement automatic repair.
6. Write docs/client-integration-handoff.md describing future UI key generation on the FIRST request, original key/payload on repeated requests, 202 saved-record confirmation and delayed search visibility. Explain conflict versus unresolved failure and that retry may not repair publication. Align validation/timezone limits; do not change Angular apps.
7. List deferred search API/consumer/query store, auth and genuine transcript export. Do not claim full PDF or end-to-end search completion.
8. Run mandatory 100% line/branch/method coverage, source/type completeness and intentional missing/empty/below-threshold checks. Document separate unit/host/integration evidence and generated-only exclusions.

## Acceptance and checks

- Run the mandatory 100% line/branch/method coverage gate with complete authored-source inclusion and unchanged justified generated-code exclusions. Report isolated unit/host coverage separately from external integration evidence.

- All chosen tests/build/container checks are recorded truthfully; report blockers.
- One job per retained key is verified; deletion removes that guarantee for later recreation and accepted-message duplication remains possible.
- Docs agree with the reduced scope and no automatic commit/push/deployment occurs.

Run appropriate build/tests and record actual results in `API/job-posting-api/ai-log/job-posting-api-work-notes.md`. Report changed files, assumptions and blockers. Do not commit, push or continue into later stages automatically.
