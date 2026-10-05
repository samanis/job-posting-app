# Final verification and search client handoff

Read shared-requirements.md and requirements-review.md in this prompt directory first. Apply every shared constraint. Execute only this stage after previous stages have passed; preserve unrelated work.

## Tasks and acceptance

Run locked restore/Release build, exact100% unit/host source/type/negative gates and real PG/Rabbit/Compose verification. Verify wire interoperability using locally frozen producer-compatible fixture, expired ingestion, available-list/detail behavior, paging under concurrency, identity conflicts/poison quarantine, commit/ACK uncertainty/redelivery, broker/DB outages, recovery and graceful/forced stop. Do not drain real developer queue for tests. Record actual results and failure injection boundaries; verify independently without posting source/database.

Finish contract, design, runbook and docs/client-integration-handoff.md. Explain GET-only routes/schema/errors/query/sort/cursor expiry and reset behavior, UTC availability versus posting timezone, eventual visibility, timestamp precision, plain text description, no automatic polling/retries, proxy/CORS integration to be scoped later. Existing Angular contract is proposed; list precise mismatches without editing frontend. Capture performance evidence, quarantine manual handling, duplicate recreation limitations, standalone setup and Git input/secret checks. List root Compose/full-stack frontend/auth/transcript/public submission gaps honestly. Genuine transcript export must come from actual tool conversation, not work-note reconstruction.

Acceptance: all chosen checks truthfully documented, coverage100% unchanged generated-only exclusions, no posting ProjectReference/source/migration/HTTP/database dependency, no full PDF completion claim, no commit/push/deploy. Report changed files/assumptions/blockers and stop.

Run appropriate build/tests and mandatory exact 100% coverage gates. Write actual commands/results and remaining limitations in API/job-search.api/ai-log/job-search-api-work-notes.md. Do not proceed to the next stage automatically.
