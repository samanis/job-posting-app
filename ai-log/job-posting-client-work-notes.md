# Job posting client work notes

These are work notes, not a chat transcript. The exercise still requires a genuine transcript export.

## Stage 1 - Angular 22 foundation (2026-10-04)

Implemented only the Angular client foundation in apps/job-posting: standalone OnPush shell, zoneless Angular defaults, lazy /jobs/new route, root redirect, not-found route, accessible skip link, strict TypeScript/template checking, local responsive CSS, optional API development proxy, and adjacent Vitest tests. No form, API, search app, database, or Docker implementation.

Node 22.23.2 and npm 10.9.8. Installed Angular framework/CLI/build 22.2.1, TypeScript 6.0.3, RxJS 7.8.2, Vitest and coverage-v8 5.0.3, jsdom 30.1.2. package-lock.json records the full dependency graph.

Setup commands from repository root:
- npm.cmd exec --yes --cache .npm-cache --package @angular/cli@22.2.1 -- ng new job-posting --directory apps/job-posting --routing --style css --strict --standalone --skip-git --skip-install --defaults

Commands from apps/job-posting:
- npm.cmd install --cache ../../.npm-cache
- npm.cmd install --save-dev @vitest/coverage-v8@5 --cache ../../.npm-cache
- npm.cmd run test:coverage
- npm.cmd run build
- npm.cmd ls --depth=0

Verification: 3 test files, 6 tests passed. Statements 40/40, branches 30/30, functions 10/10, lines 13/13: all 100%. Six production TypeScript files covered, including main.ts. Build passed with two lazy page chunks. Angular's supported unit-test builder enforces per-file 100% thresholds. Coverage includes src/**/*.ts; excludes only src/**/*.spec.ts, src/**/*.test.ts, and src/**/*.d.ts. CSS/HTML behavior is checked through component tests, not counted as TypeScript coverage. No real-browser visual review was performed in this foundation stage.

Development: from apps/job-posting, run npm.cmd start. Root redirects to /jobs/new. Set JOB_POSTING_API_URL to the future API origin before starting to enable /api/** forwarding; without it the proxy is empty. No backend is available yet. No deployed origin is hardcoded. npm.cmd is used because the machine blocks npm.ps1 execution.

The CLI-generated README was removed to honor the user's instruction. npm cache is ignored at repository root. Restricted compiler directory access required running checks with approved sandbox escalation. No commits, pushes, or publishing performed.

## Stage 2 - API contract and response handling (2026-10-04)

Added readonly request/saved-record contracts, discriminated PostingOutcome, runtime saved-record validation, server validation mapping, Retry-After parsing, and injectable JobPostingApi. Registered provideHttpClient. Default timeout 15 seconds, overrides bounded to 1?60 seconds; injectable clock supports deterministic delays. No automatic POST retries, form wiring, idempotency store, or backend implementation. docs/api-contract.md marks all backend expectations as proposed. Existing routing remains because removal was discussed but not requested in this execution stage.

Consulted official Angular HttpClient request/testing documentation. HTTP tests assert exact URL, POST body, caller key, returned saved record, failures, timeout cancellation, and absence of retries. Classifier tests cover malformed bodies, date boundaries, validation aliases, conflict variants, throttling and all fallback paths. Corrected a test setup error by applying provider overrides before TestBed instantiation.

Verification command from apps/job-posting: npm.cmd run test:coverage. Result: 5 test files, 86 passing tests, 100% statements (134/134), branches (132/132), functions (26/26), lines (77/77), with unchanged coverage thresholds/exclusions. Type-only contracts have no runtime code. Production build verification recorded below. These work notes remain distinct from the required genuine chat transcript.

Stage 2 production verification: npm.cmd run build passed with strict TypeScript and template checking. No commits or pushes performed for this stage.

## Stage 3 - Signal Form and validation (2026-10-04)

Implemented Angular 22 Signal Forms in NewJobPage with all seven required inputs, explicit labels/help/errors, an error summary, invalid-submit focus, typed validPayload output, and external field/form message support. No HTTP calls, submission lifecycle, or client idempotency store added. Salaries remain text until validation passes, using decimal input mode to preserve blank and precision-sensitive values; payload normalization emits numbers and trimmed text. Schema validators enforce whitespace requirements, nonnegative finite salary values with at most two decimal places, strict salary range, and valid future local calendar dates. LOCAL_CLOCK is injectable; submission refreshes today's date to handle midnight without UTC shifts. Editing a field permanently removes only its stale server messages; general and other field errors are retained. Existing router remains unchanged.

Consulted Angular's official Signal Forms overview/validation documentation and installed type declarations. Compiler correctly rejected manually setting required on FormField controls; the form now uses aria-required with schema validation. Tests cover malformed salaries, blank values, relationship boundaries, invalid/leap/year-boundary dates, local timezone components, midnight rollover, rendered controls/messages, focus, output normalization, and external message clearing/text escaping. No real browser visual review performed.

Verification: npm.cmd run test:coverage passed, 7 test files and 122 tests. Coverage: statements 210/210, branches 186/186, functions 45/45, lines 137/137, all 100%, with unchanged thresholds/exclusions. Production build result recorded after execution. No commits or pushes performed. These are work notes, not a genuine transcript export.

Stage 3 production verification: npm.cmd run build passed with strict TypeScript/template checking and component style budgets.

Test organization update: moved all seven client spec files to apps/job-posting/tests, mirroring src. Updated relative imports, Angular test discovery and tsconfig.spec.json. Future prompts now require this layout. Verified 122 tests pass and all four coverage metrics remain 100%.

## Stage 4 - Client idempotency and recovery (2026-10-04)

Added feature-local PostingAttemptStore and versioned persistence format under features/job-posting/state. UUID, sessionStorage and clock adapters are injectable. begin grants dispatch permission only after durable snapshot storage; retry returns the original frozen key/payload with no draft argument. Read-only signals expose the attempt, recovery problem and editing lock. Outcomes settle only the current in-flight key; stale responses are ignored. Unknown/pending outcomes retain identity, throttling honors retryAt, definite rejections permit a fresh key, conflicts require explicit reconciliation, and confirmed success requires postAnother before a new attempt. Confirmed success remains in memory if persistence/cleanup fails. Recovery decodes and validates stored records, treats prior in-flight as unknown, and blocks corrupt/unavailable storage without blind clearing. No automatic expiry, retries, HTTP calls, or form wiring.

Tests remain under apps/job-posting/tests. Added deterministic tests for frozen snapshots, ordered payload identity, lifecycle transitions, recovery, key stability across unknown reasons, concurrent dispatch permission, throttle boundaries, stale responses, storage read/write/remove failures, malformed schema/status/deadlines/payloads, and UUID errors. Default browser adapters are tested separately. Documentation: apps/job-posting/docs/client-idempotency.md explains integration, explicit reconciliation obligations, storage scope and required backend atomic idempotency. Angular signal documentation consulted for read-only state; runtime freezing prevents mutable payload snapshots.

Final verification from apps/job-posting: npm.cmd run test:coverage passed 9 files and 197 tests; statements 322/322, branches 281/281, functions 67/67, lines 224/224 (all 100%, unchanged coverage exclusions/thresholds). npm.cmd run build passed. Existing user changes preserved. No commits/pushes, backend work, or UI wiring performed. Work notes are not a transcript export.

## Stage 5 - Submission workflow and retries (2026-10-04)

Added page-scoped signal-based PostingWorkflow and connected it to NewJobPage, JobPostingApi and PostingAttemptStore. The form validates before obtaining dispatch permission, locks controls for unresolved attempts, sends one POST per permitted attempt, shows typed outcomes and server field/form validation, and retains the actual API saved record. Explicit retries send the original key/body, never editable draft values. Recovery hydrates the stored payload without startup HTTP. Pending/unknown/conflict/recovery states are distinct; conflict/reset requires explicit prior-outcome reconciliation. Throttle countdown uses the injected API clock and RxJS interval, refuses early retries, and stops on deadline or page teardown. Request teardown cancels transport but preserves unknown outcome for recovery. Saved state remains saved if persistence/cleanup fails. Confirmation details remain stage 6. No backend/authentication implementation, automatic POST retry, or lazy route changes.

Tests under apps/job-posting/tests add full HTTP/component integration for successful and malformed 2xx, validation 400/422, generic 4xx, conflict variants, 429 delays/fallbacks, 5xx, network errors, timeouts, rapid submissions, corrections with fresh keys, unchanged retry bodies/keys, recovered states, interrupted requests, timer cleanup and storage failures. Existing form tests now verify the transport seam. Fake timing controls only interval timers to avoid blocking Angular rendering; teardown assertions verify the countdown handle rather than jsdom's requestAnimationFrame intervals. Consulted official Angular takeUntilDestroyed documentation and installed Signal Forms readonly APIs.

Final verification: npm.cmd run test:coverage passed 10 files and 227 tests. Statements 420/420, branches 337/337, functions 85/85, lines 302/302, all 100%, with unchanged thresholds/exclusions. npm.cmd run build passed. Existing user edits preserved. No real-browser visual review or real API verification performed; the API is still a proposed integration contract. No commits or pushes. These work notes are not a chat transcript.

## Stage 6 - Saved record confirmation and accessibility (2026-10-04)

Added standalone OnPush SavedJobConfirmation with a required signal input and postAnother output. It renders all API-saved fields, identifier, numeric salaries without assumed currency, local calendar closing date, explicitly labeled UTC saved timestamp, and multiline plain-text description. NewJobPage replaces the form after success, focuses the confirmation heading after rendering, and exposes a Post another job action that clears the saved attempt before resetting the form and focusing title. Cleanup failure preserves confirmation and the saved record. Recovered saved records also render confirmation without startup HTTP.

Completed responsive card/details styling, readable status/error wrapping, minimum-height buttons, disabled/read-only styles and checkbox sizing. Existing labels, help/error associations, polite submission status, keyboard-native buttons and invalid-submit focus remain. No per-keystroke alert announcements or raw diagnostic rendering introduced. Tests cover actual server record differences, escaped HTML-like description, signal input updates, date/number formatting, focus, saved cleanup failure, and fresh attempt identity after Post another job.

Verification: npm.cmd run test:coverage passed 11 files and 231 tests; statements 441/441, branches 349/349, functions 91/91, lines 315/315 (all 100%; unchanged thresholds/exclusions). npm.cmd run build passed. A locale whitespace assertion was normalized, and confirmation focus uses the host ElementRef to select its fixed heading rather than an unnecessary compiled view query. No coverage exclusions added.

Visual QA limitation: started the local Angular preview and attempted browser inspection, but the in-app browser was unavailable and cua.listBrowsers returned an empty list. Narrow/wide real-browser visual review could not run. Responsive CSS and rendered behavior were tested but are not a substitute for that visual check. The temporary preview process was stopped. No commits/pushes or additional application scope. Work notes remain separate from a genuine transcript export.

## Stage 7 - Full unit test coverage audit (2026-10-04)

Audited coverage scope, per-file thresholds, bootstrap/configuration, response handling and idempotency/validation/accessibility assertions. Proved coverage includes unimported sources by temporarily introducing coverage-audit-probe.ts: all 231 existing tests passed but the gate exited 1 for its uncovered function/statement/line. Removed the probe and retained existing thresholds/exclusions. Added explicit recovery-checkbox and recovered idempotent replay tests. Made the form test UUID deterministic, verified its outstanding HTTP requests, and froze the default-clock test's Date. No suppression comments, skipped/focused tests or arbitrary sleeps found. No production behavior changed for this stage.

Documented commands, exact globs, gate failure evidence, test matrix and scope limitations in apps/job-posting/docs/testing-and-coverage.md. Coverage is production src TypeScript; type-only contracts have no executable code. HTML/CSS behavior, actual browser layout and backend guarantees are not implied by percentages. Browser visual review remains outstanding.

Final verification: npm.cmd run test:coverage passed 11 test files and 233 tests with 100% statements 441/441, branches 349/349, functions 91/91 and lines 315/315; no uncovered source files/branches. npm.cmd run build passed with strict checks and budgets. No commits or pushes performed. These work notes are not a transcript export.

## Stage 8 - Client verification and documentation (2026-10-04)

Audited the final client against required fields/validation, authoritative saved confirmation, server error handling, Angular 22 features, client retry identity and unit coverage. Added docs/local-development.md (no README), including prerequisites, lockfile install, development/proxy commands, build/test commands, actual output locations, current routes/source layout, accessibility behaviors, limitations and genuine transcript instructions. Finalized api-contract.md to describe implemented client lifecycle and outstanding backend agreements, including date semantics for replay and key retention. Confirmed current changes are client tests/docs and work notes; no search/API/database/Docker/authentication implementation added.

Verified reproducible npm.cmd ci installation from package-lock.json: 277 installed/278 audited, no reported vulnerabilities. Final npm.cmd run test:coverage passed 11 files and 233 tests with 100% statements 441/441, branches 349/349, functions 91/91, lines 315/315. Final npm.cmd run build passed strict TypeScript/template checks and budgets. HttpTestingController fixtures cover successful posts, server validation and unknown-outcome retries; no mock server or actual backend was introduced. git diff --check passed and no tracked README files were added.

Client implementation and unit/build checks are complete. Real API integration and browser visual validation remain outstanding. Full exercise still requires the search Angular app, both .NET 10 APIs, EF Core persistence/migration, inter-app availability, root Docker Compose, a genuine chat transcript and final repository submission. Transcript instructions describe capturing actual messages; work notes are not substituted for the transcript. No commits, pushes, publishing or recruiter contact performed in this stage.

## Playwright E2E addition (2026-10-04)

User explicitly authorized Playwright browser tests. Added @playwright/test and Node 22 typings, playwright.config.ts, tsconfig.e2e.json and e2e/job-posting.spec.ts. Nine flows run in desktop and Pixel 7 Chromium projects (18 checks), using API request interception against the real Angular page and fixed browser dates. Covered field validation, confirmation/plain-text description/date, saved refresh, keyboard Post another job, server errors/corrected keys, unknown/pending refresh retry identity, rapid-click blocking, deterministic throttle delay and keyboard focus/viewport overflow. No backend dependency or automated POST retries. Managed test server uses port 4300 with reuse disabled and stops after execution. Test reports/traces/screenshots are ignored by Git. Added install/run/headed/report/typecheck scripts and README/testing documentation.

Verification: Chromium installed via official Playwright download. npm.cmd run test:e2e passed 18 tests in 19.9s. npm.cmd run test:e2e:check passed strict type checking after adding @types/node@22. npm.cmd run test:coverage still passed 233 unit tests, 100% all four metrics (441 statements, 349 branches, 91 functions, 315 lines). npm.cmd run build passed. Inspected generated full-page form/error-state screenshots at desktop and mobile sizes: no visible clipping or horizontal overflow. This limited visual review does not cover all states, contrast or all browsers. Backend correctness and Firefox/WebKit remain unverified. No commits or pushes.

## Visual polish (2026-10-04)

Refined typography, header alignment, form card spacing and controls. Salary fields share a desktop row and stack on mobile. Validation summary links to affected fields while detailed messages remain inline. Submission status uses success, error and pending colors. Captured and visually inspected desktop/mobile form validation and saved confirmation screenshots; no visible clipping. This is a limited visual review, not a full accessibility audit.

Verification: 18 Playwright tests passed, 233 unit tests passed with all four coverage metrics at 100%, and production build passed. No commits or pushes.
