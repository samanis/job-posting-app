# Client tests and coverage gate

Run from apps/job-posting:

- npm.cmd test ? complete unit suite, non-watch.
- npm.cmd run test:coverage ? complete suite with enforced coverage and HTML/text/JSON-summary reports.
- npm.cmd run build ? production compilation with strict TypeScript/templates and bundle/style budgets.

Angular 22's supported @angular/build:unit-test builder runs Vitest 5 with jsdom and @vitest/coverage-v8. Specs live in tests/ mirroring src/. Discovery is ../tests/**/*.spec.ts relative to sourceRoot src; tsconfig.spec.json includes tests/**/*.spec.ts and src/**/*.d.ts.

## Coverage scope

coverageInclude: src/**/*.ts.
coverageExclude: src/**/*.spec.ts, src/**/*.test.ts, src/**/*.d.ts.

No production TypeScript is excluded. This covers main.ts bootstrap, app.config.ts, routes, page/confirmation components, API service/classifier, validators/models, attempt format/store, and workflow. Type-only contract files have no executable statements and are reported empty. Dependencies/generated build output are outside the source include. HTML/CSS, proxy.conf.cjs and framework internals are not counted as production TypeScript coverage; components exercise template behavior but percentages are not template/CSS coverage.

Thresholds: perFile=true, statements=100, branches=100, functions=100, lines=100. A 100% per-file requirement also enforces 100% aggregate coverage. Threshold failures produce a nonzero Angular CLI exit. No suppression comments or reduced thresholds.

Stage 7 verified untouched-file enforcement by temporarily adding an unimported src/coverage-audit-probe.ts function. All existing tests passed, but coverage reported that file at 0% and exited 1. The probe was removed afterward; it is not part of the application. Existing development runs also demonstrated branch-gate failure before missing assertions were added. Bootstrap behavior is tested with mocked bootstrapApplication success/failure; it is included rather than exempted.

## Behavior and determinism

Tests use Angular TestBed, HttpTestingController, injected storage/key/clock adapters, and controlled timers. HTTP mocks assert URL, POST payload and Idempotency-Key, and verify no extra requests. Fixture-level tests check rendered validation, readonly/disabled controls, recovery actions, polite status semantics, text escaping, focus and saved-record display. Real default clock/UUID adapters are checked with controlled globals. No network backend is needed or invoked. No arbitrary sleeps, skipped/only tests or coverage suppression comments.

Response coverage includes saved 200/201 and other complete 2xx; accepted 202; empty/malformed success including 204 and parse failure classification; validation 400/422 with known/unknown/malformed fields; named/general 409; 429 valid/invalid/missing delay; other generic 4xx; 5xx; network failure and timeout. No authentication/authorization-specific behavior or tests.

Idempotency coverage includes rapid clicks, locked draft edits, retry identity/body preservation, recovered attempts and saved records, interrupted requests, rejection correction with fresh keys, conflict resolution, invalid recovery records, throttle boundaries, and storage read/write/remove failures. Validation includes whitespace, missing/nonfinite/negative/overprecision salary, bounds, invalid/leap/local/year-boundary dates and midnight. Confirmation uses API values and never HTML rendering for descriptions.

100% measures execution, not correctness or backend guarantees. At-most-once persistence still requires backend atomic idempotency. Browser keyboard/visual/contrast and cross-timezone end-to-end behavior need real browser validation; narrow/wide visual review remains unavailable because no browser is connected. CSS responsiveness and native keyboard semantics are implemented but not proven by the coverage metric.

## Playwright browser tests

Specs live under e2e/, separately from unit tests. Install Chromium once with npm.cmd run test:e2e:install, then run npm.cmd run test:e2e. playwright.config.ts starts/stops an isolated Angular server on 127.0.0.1:4300 and runs Desktop Chrome and Pixel 7 emulation using Chromium. Port 4300 must be free; the suite will not reuse an unrelated server. Browser contexts isolate sessionStorage between tests.

Nine flows run on both projects (18 tests): required/range/date validation; API confirmation/plain text/recovered success/post-another keyboard action; server field/general errors and corrected identity; server/network/accepted outcome retries across refresh; concurrent submit blocking; controlled Retry-After countdown; keyboard invalid-submit focus and viewport overflow. API route interception supplies contract fixtures and captures payloads/keys. Browser clock fixtures make dates and delays deterministic. No automatic test retries or arbitrary sleeps. Native browser UUIDs are asserted for presence and lifecycle stability rather than compared with a fixed random value.

Use test:e2e:check for strict TypeScript checks, test:e2e:headed for visible execution and test:e2e:report for the HTML report. test-results/ and playwright-report/ are ignored. Failures retain traces/screenshots; the viewport test also captures a full-page form screenshot. E2E percentages do not contribute to unit coverage. Real backend at-most-once guarantees and Firefox/WebKit compatibility are not verified by these mocked Chromium tests.

Browser execution is now available through the explicitly requested Playwright setup. Automated desktop/mobile focus and overflow checks pass; this does not constitute a full accessibility or contrast audit.
