# Job posting client prompts

Run the numbered prompts in order by asking: `Run prompts/job-posting-angular/01-angular22-foundation.md`.
Each prompt requires reading this file first. Only implement the selected stage; do not execute later prompts automatically.

## Shared instructions for every stage

- Scope: the Angular job posting client in `apps/job-posting` only. Do not implement the search app, APIs, database, Docker, authentication, or authorization. Backend behavior below is a proposed integration contract, not an existing capability.
- Inspect existing files and applicable AGENTS.md instructions first. Preserve user changes. Build on completed stages instead of regenerating them.
- Use Angular 22 stable releases and compatible dependencies. Verify official Angular documentation and installed APIs before using them. Do not silently downgrade or use prereleases. If Angular 22 cannot be installed, report the concrete blocker.
- Use standalone components, zoneless change detection, signals/computed state, signal inputs/outputs where applicable, built-in template control flow, lazy routing, strict TypeScript and template checks, and OnPush. Prefer Angular 22 Signal Forms for this form. Use HttpClient for the POST mutation; do not use a resource API that might repeat a mutation reactively. Avoid introducing features just to demonstrate them.
- Test each stage as it is implemented. Use the Angular CLI supported Vitest setup and Angular testing utilities. The final gate is 100% statements, branches, functions, and lines for all authored production TypeScript, including bootstrap/configuration where instrumentable. Include untouched source files in coverage. Only dependency, generated, declaration, and test files may be excluded; document exact exclusions. No coverage suppression comments, disabled tests, threshold reductions, or tests that merely reproduce the implementation.
- Coverage measures executed code, not correctness. Assert rendered behavior, requests, outcomes, and edge cases. Test templates through components even though template coverage is not measured as TypeScript coverage.
- Keep styling local and accessible; prefer native controls and a small responsive design over additional UI libraries. Render description as plain text. Do not add authentication, currency selectors, rich text editing, or job management features.
- Do not publish, push, send messages, or fabricate AI transcripts. Keep a truthful stage summary in `ai-log/job-posting-client-work-notes.md`, labeled work notes, not a chat transcript. A genuine transcript export is still required by the exercise.
- Run the available stage tests and build checks. Report changed files, checks and results, assumptions, and any blocker. Never claim a check passed unless it ran successfully.

## Exercise requirements

Required inputs: job title, department, location, description, salary minimum, salary maximum, closing date. Salary minimum must be strictly less than maximum. Closing date must be in the future. Validate before POST, show server validation messages on the form, and show the saved record returned by the API after success.

## Client contract and decisions

Document these in `apps/job-posting/docs/api-contract.md` during stage 2. They can be adapted to a real backend later.

- POST `/api/jobs` with `Idempotency-Key` and JSON fields `title`, `department`, `location`, `description`, `salaryMin`, `salaryMax`, `closingDate`.
- Saved response additionally includes `id` (string) and `createdAt` (ISO timestamp). A completed save returns 201, or 200 for an idempotent replay, with the complete saved record. Other 2xx responses without a valid saved record do not establish completion.
- 202 is pending, not saved. There is no status endpoint in scope: preserve the attempt and explain that completion is unconfirmed. 204 or malformed 2xx is also unconfirmed. Replaying a POST is safe only if the backend honors the idempotency contract.
- 400/422 may carry ASP.NET-style validation Problem Details with `errors` keyed by field. Treat errors on unknown fields as form-level messages. Support PascalCase and camelCase field names. Invalid/non-JSON error bodies use safe fallback text.
- 409 with code `idempotency_key_conflict` means a key was used with a different payload; do not silently generate a new key and retry. Code `idempotency_in_progress` means pending and permits a later same-attempt retry. Other 409 responses are a general conflict.
- 429 is throttled; respect a valid Retry-After delay before manual retry. Network failures, timeouts, and 5xx have uncertain outcomes. Keep the original key and exact payload. No automatic POST retries.
- Other 4xx get a clear general error. 401/403 need no special behavior, interceptors, redirects, or tests; they may fall through the generic response classifier.
- Backend obligation: atomically associate a key and payload with at most one saved job, serialize concurrent duplicate attempts, replay the saved response, and reject key/payload mismatch. Client code cannot enforce this server guarantee. Backend key retention must cover the client retry lifetime; until agreed, do not automatically expire pending client attempts.
- Closing date is `YYYY-MM-DD`, strictly later than today's calendar date in the user's local timezone. Inject a clock for deterministic tests; avoid UTC conversion of date-only strings. Revalidate at submission, including after midnight.
- Trim required text and reject whitespace-only input. Salaries are finite decimal numbers with at most two decimal places, nonnegative, and min < max. These are explicit client assumptions to align with the backend. No currency is specified by the exercise.
- Client idempotency is per logical submission in one browser tab, with sessionStorage recovery across refresh. It does not deduplicate independently entered jobs across tabs/devices or identify duplicates by content.

## Sequence

1. `01-angular22-foundation.md` — workspace and test foundation.
2. `02-api-contract-and-response-handling.md` — transport contracts and status classification.
3. `03-signal-form-and-validation.md` — accessible form and validators.
4. `04-client-idempotency-and-recovery.md` — attempt identity, payload preservation, recovery.
5. `05-submission-workflow-and-retries.md` — connect state, transport, and form.
6. `06-saved-record-confirmation-and-accessibility.md` — saved record and final user experience.
7. `07-full-unit-test-coverage.md` — close gaps and enforce 100%.
8. `08-client-verification-and-documentation.md` — final audit and local instructions.

## Official references

- Angular Signal Forms: https://angular.dev/guide/forms/signals/overview
- Angular 22 stable form API: https://angular.dev/api/forms/signals/form
- Signal Forms testing: https://angular.dev/guide/forms/signals/testing
- Angular compatibility: https://angular.dev/reference/versions
- Angular testing: https://angular.dev/guide/testing

Verify these again when executing, since documentation and dependency versions can change.
