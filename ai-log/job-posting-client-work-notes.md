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
