# Job posting client development

The Angular job posting client is implemented through stages 1-8. The full take-home exercise is not complete: the search app, two .NET APIs, persistence/migration, Docker Compose, genuine AI transcript, and final repository submission still need work. Backend integration and real-browser visual review remain outstanding.

## Prerequisites and install

Verified environment: Node 22.23.2, npm 10.9.8. Installed Angular framework/CLI/build 22.2.1, TypeScript 6.0.3, RxJS 7.8.2, Vitest/coverage-v8 5.0.3. Use the committed package-lock.json to reproduce dependencies. Angular 22 CLI requires Node ^22.22.3, ^24.15.0 or >=26.0.0; use an Angular-supported release.

From repository root in PowerShell:

```powershell
cd apps/job-posting
npm.cmd ci
npm.cmd start
```

npm.cmd avoids the machine's blocked npm.ps1 execution policy. On shells without that restriction, npm works normally. Open http://localhost:4200. The root redirects to /jobs/new; unknown paths show a return link. The current app retains its existing lazy route shell, though the posting and confirmation experience stays on one page. A second frontend is not started by these commands.

## Connect the future API

No API server is included in this client deliverable. Without a proxy target, the form can be reviewed locally but submission cannot save a job. An unconfirmed response remains locked for a same-attempt retry; do not reset an uncertain outcome just to create a new key.

Set JOB_POSTING_API_URL to the actual job posting API origin before starting the dev server. Example only:

```powershell
$env:JOB_POSTING_API_URL = 'http://localhost:5000'
npm.cmd start
```

proxy.conf.cjs forwards /api/** to that origin with changeOrigin; it does not rewrite /api/jobs. Restart the dev server after changing the environment variable. The port is an example, not an implemented backend endpoint. For production, serve /api/jobs through the deployment's same-origin reverse proxy; the development proxy is not bundled into production. Backend responses must match api-contract.md and implement atomic idempotency before uncertain retries are safe.

## Verify and build

From apps/job-posting:

```powershell
npm.cmd test
npm.cmd run test:coverage
npm.cmd run build
```

Optional interactive commands: npm.cmd run test:watch and npm.cmd run watch. Coverage already executes the full non-watch suite. Tests use HttpTestingController fixtures, not a mock server or a live API. Production output is dist/job-posting/browser. Coverage output is coverage/job-posting; open its index.html for the report. Per-file 100% statements/branches/functions/lines is enforced, including untouched src TypeScript.

## Source organization

- src/app/core/api: readonly contracts, HTTP service, timeout/clock configuration and response classification.
- src/app/features/job-posting/components: Signal Form page and saved-record confirmation/templates/styles.
- src/app/features/job-posting/models and validators: draft structure, normalization, salary/date validation and local clock.
- src/app/features/job-posting/state: immutable attempts, sessionStorage adapters and page-scoped submission workflow.
- tests: mirrors source organization; all client specs live here.
- docs: development, proposed API contract, idempotency and test coverage notes.
- prompts: staged implementation prompts and shared requirements.
- ../../ai-log: AI chat transcripts and work notes for the whole repository; see its README.

## Angular and user behavior

Standalone OnPush components keep composition explicit. Signal Forms models fields and reactive validation. Signals/computed values derive UI state, readonly fields, retry availability and countdowns. Required signal input/output separates authoritative confirmation display from page reset. Built-in @if/@for/@switch handles conditional states. Angular runs zoneless; strict TypeScript/template checks are enabled. HttpClient performs the explicit POST mutation; RxJS teardown cancels requests and countdowns when the page is destroyed. Lazy routing is retained from the foundation, not required for the single-page workflow.

Every field is required; whitespace text is invalid. Salaries must be nonnegative finite amounts with at most two decimals and minimum strictly below maximum. No currency is assumed. Closing date must be a valid YYYY-MM-DD after the user's local today; validation refreshes on submit to handle midnight. Confirmation displays the record returned by the API, numeric salary values, a local-calendar closing date and a labeled UTC saved timestamp. Descriptions/server messages are plain text.

Labels, help/error associations, invalid-submit focus, a polite submission live region, native keyboard-operable buttons, visible focus styles and responsive layouts are implemented. Confirmation receives focus after save; Post another job clears the completed attempt and focuses title. Cleanup failure leaves saved confirmation intact. No field errors are announced as alerts on every keystroke. Narrow/wide real-browser layout, contrast and full keyboard review remain unverified because browser tooling had no connected browser.

## Duplicate submission protection

One logical attempt contains a UUID key, immutable normalized payload and versioned lifecycle status. It is persisted under sessionStorage key job-posting.attempt.v1 before dispatch. Concurrent clicks cannot dispatch again. Uncertain/network/timeout/server/pending outcomes keep the original key/body; retry is manual and respects valid Retry-After delays. Refresh restores the snapshot without automatically POSTing. Malformed/unavailable storage blocks new posting; conflict/recovery reset requires explicit prior-server-outcome reconciliation. Definite rejection permits correction with a fresh key. Confirmed success requires Post another job to start again.

This protects one tab's attempt/retry lifecycle; it does not identify independently entered duplicate jobs across tabs/devices. At-most-once saves require backend key/payload enforcement, response replay and sufficient retention. See client-idempotency.md.

## AI transcripts

The chat transcripts and work notes for this app and the rest of the repository are in [ai-log](../../../ai-log/README.md).

## Integration

This client runs against the Job Posting API started by the repository's root `docker compose up`; see the [root README](../../../README.md).

## Final verification (October 4, 2026)

npm.cmd ci completed from the committed lockfile (277 packages installed, 278 audited, no reported vulnerabilities). npm.cmd run test:coverage passed all 233 tests in 11 files: statements 441/441, branches 349/349, functions 91/91, lines 315/315, all 100%. No uncovered production TypeScript files/branches. npm.cmd run build passed strict compilation and configured budgets. Successful POST, server validation and uncertain-outcome retry flows were verified through HttpClient fixtures. Actual backend integration and real-browser visual review were not performed.
