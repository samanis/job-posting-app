# Stage 7: Full unit coverage gate

Read `prompts/job-posting-angular/shared-requirements.md` first. Stages 1–6 must exist; implement only this stage.

## Tasks

1. Audit production sources against the requirements and the generated coverage report. Include all authored production TypeScript, not only imported files. Document exact coverage include/exclude globs.
2. Enforce global and per-file 100% statements, branches, functions, and lines through supported tooling. Make the command fail when any threshold is missed. If tooling cannot instrument a framework bootstrap artifact, explain the exact limitation rather than silently excluding it or claiming total coverage.
3. Add behavior-focused tests for uncovered paths in validation, transport parsing, state transitions, storage, dates, timers, routing, confirmation, and form components. Test the production configuration/entry seam where practical.
4. Check the response matrix: 200/201 saved and replay, other valid/invalid 2xx, 202/204, 400/422 field and general errors, 409 variants, 429, other generic 4xx, 5xx, network failure, and timeout. No dedicated 401/403 feature tests are needed.
5. Check idempotency invariants across rapid submits, retries, edited inputs, refresh, interrupted requests, corruption, and persistence cleanup failure. Include salary/date boundary and accessibility behavior tests.
6. Remove flaky sleeps, uncontrolled dates/UUIDs, accidental real HTTP, skipped tests, coverage suppression comments, and unnecessary source branches. Do not distort production behavior merely to increase a metric.

## Acceptance and checks

- Run the non-watch complete unit suite, strict checks, production build, and coverage command.
- Report actual percentages for all four metrics and any uncovered files/branches.
- 100% is a hard requirement; if it is not achieved, report the stage incomplete rather than adjusting thresholds.
- Backend guarantees are mocked contract assumptions, not proof of server idempotency.
