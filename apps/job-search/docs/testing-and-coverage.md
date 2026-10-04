# Unit testing and coverage

From the repository root:

```powershell
npm.cmd --prefix .\apps\job-search run test
npm.cmd --prefix .\apps\job-search run test:coverage
npm.cmd --prefix .\apps\job-search run build
```

The independent Angular 22 project uses @angular/build:unit-test with Vitest 5 and V8 coverage, jsdom and Angular testing utilities. Specs live under tests, mirroring production paths. Discovery is ../tests/**/*.spec.ts relative to sourceRoot; tsconfig.spec.json includes tests/**/*.spec.ts and src/**/*.d.ts. Tests are not stored under src.

## Enforced gate

angular.json includes src/**/*.ts, even source not imported by tests. Exact explicit exclusions: src/**/*.spec.ts, src/**/*.test.ts, src/**/*.d.ts (test/declaration files only). Dependencies, generated build/cache output and E2E files are outside this production-source inclusion. No authored executable production file is individually excluded. Type-only query-contract.ts has no executable code and is reported as an empty file, not a coverage exception.

coverageThresholds has perFile: true, with statements, branches, functions and lines all 100. Bootstrap, route/configuration code and injectable default factories are covered. Reports: text, html and json-summary under coverage/job-search. No suppression comments, skipped/focused tests, weakened thresholds or production-only coverage accommodations were found in the audit.

On 2026-10-04, a temporary src/coverage-gate-probe.ts exported an unimported function with two branches. All 188 tests passed but test:coverage exited 1: the probe had 0% for all four metrics and failed every per-file threshold. Aggregate results fell to 479/480 statements, 388/390 branches, 122/123 functions and 317/318 lines. This proves untouched files are included. The probe was then removed before final verification.

## Behavior matrix

Stage 7 verification: 189 tests passed across 15 files, including the query-bearing card-link regression found by browser testing. All 17 executable source files fully covered: statements 479/479, branches 388/388, functions 122/122 and lines 317/317 (100% each). Production build passed strict checks and budgets. The temporary probe is absent.

- HTTP: cold GET/defaults/encoding, invalid input without dispatch, complete/empty/malformed/unsupported responses, detail identity, query errors, cursor expiration, unavailable details, throttle, network/timeouts/server/client failures and cancellation without retry.
- Schemas: real calendar dates, timestamp bounds, required/bounded text, salary decimal/order/finite limits, duplicate identities, exact 50-item page and cursor bounds; immutable contracted snapshots.
- URL/form: defaults, canonical round trips, repeated/invalid parameters, debounce request counts, immediate submit, Clear, IME for all text controls, sort/limit/cursor reset, navigation restoration without echoes and teardown.
- Pagination: deep links, forward/back visits, loops, eviction and 50-entry cursor history.
- Reads: shared in-flight ownership, final-subscriber cancellation, reentrant completion, exact TTL, failed refresh does not extend freshness, default combined 50-entry list/detail LRU, key isolation, refresh bypass, failure non-caching and unavailable-detail invalidation.
- Workflows/UI: latest query/id wins, all data/loading/error/freshness states, throttle deadlines, safe descriptions/date labels, authoritative details, stable card DOM without per-card reads, titles, heading/card return focus and no focus theft on refresh.

Clocks and debounce timers are controlled. Component tests leave animation-frame scheduling real for Angular zoneless stabilization. Assertions focus on DOM, URLs, HTTP requests and outcomes rather than private cache contents. HTTP fixtures are controlled responses, not a backend implementation.

## Limits

100% applies to authored executable TypeScript; HTML/CSS are exercised through component behavior, not numerically instrumented. Unit coverage does not prove screen-reader compatibility, complete accessibility, browser history fidelity, backend correctness, cross-app visibility or server scale. Playwright now verifies selected actual browser flows; real API/load checks require a future backend.

## Playwright

From the repository root:

```powershell
npm.cmd --prefix .\apps\job-search run test:e2e:install
npm.cmd --prefix .\apps\job-search run test:e2e:check
npm.cmd --prefix .\apps\job-search run test:e2e
npm.cmd --prefix .\apps\job-search run test:e2e:report
```

The install command downloads Chromium when needed. test:e2e:headed runs visible browsers. E2E has separate strict TypeScript configuration. playwright.config.ts starts/stops an isolated Angular server on 127.0.0.1:4302, refuses reuse of an existing server, uses two workers and no retries, and forbids focused tests. Reports under playwright-report and screenshots/traces under test-results are ignored by Git. HTML report does not automatically open. Failures retain Playwright traces/screenshots; performance scenario always attaches CDP trace and JSON measurements.

18 scenarios run on Desktop Chrome and Pixel 7 Chromium, 36 checks total. Final run passed all 36 in 21.6 seconds. They cover real page navigation, preserved criteria/card focus, safe full details, debounce/Enter/Clear/IME request counts, delayed query races, pagination and Back/Forward, sort/limit changes, refresh discovering mocked new data, unavailable direct links, empty/error distinction, 202/malformed/400/422/500/network recovery, 429 deadline, keyboard-only navigation, long-text overflow and bounded page rendering/performance evidence. API routes are intercepted fixtures; no backend or posting/search integration is demonstrated. No arbitrary sleep calls are used; timing uses controlled clock/request gates.

The browser suite exposed a query-bearing RouterLink string being treated as an encoded path. Cards now bind a parsed UrlTree; a unit regression verifies actual filtered-card activation and return focus. Desktop/mobile results/details/empty/error screenshots were inspected without visible clipping. No automated full accessibility audit, Firefox/WebKit run or real-device validation is claimed. Performance evidence and scope limits are in query-performance.md.
