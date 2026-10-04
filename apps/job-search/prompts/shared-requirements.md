# Job search client: shared requirements

Read this file before every numbered prompt. Run stages in order, implementing only the requested stage. Prompts live in apps/job-search/prompts and must survive CLI scaffolding.

## Scope and source

The exercise requires a separate Angular 22 project that lists available jobs, opens their full details, and displays jobs created in the posting app. Eventual consistency is acceptable. The exercise describes high-volume search and detail reads. These are document requirements, not permission to implement backend services or publish anything.

The user's current scope is the search client only. Do not implement APIs, databases, inter-app messaging, Docker, authentication, authorization, or changes to the posting app. Do not pretend mocked responses demonstrate cross-app integration. Search filters, pagination, cache policy and endpoints below are proposed client design decisions, not requirements stated by the PDF or an agreed backend contract.

## Engineering rules

- Inspect applicable AGENTS.md, existing files and completed stages first; preserve user changes and these prompts. Keep this an independent project with its own package.json and lockfile under apps/job-search.
- Use stable Angular 22 and compatible dependencies. Verify official Angular documentation and installed APIs at execution time. Do not silently downgrade or install prereleases. Use standalone components, zoneless change detection, OnPush, signals/computed, signal inputs/outputs, built-in control flow and strict TypeScript/templates. Use features where they solve a concrete problem.
- Use HttpClient and a single owned RxJS query pipeline for cancellation, debounce and deduplication. Do not introduce a second resource subscription that repeats requests. Signal Forms may be used for filters if the installed stable APIs suit the behavior.
- Use /jobs and /jobs/:id routes with deep links and lazy detail loading. URL state owns committed query criteria; the text input draft may differ while debouncing. Preserve query and pagination on return from details.
- All queries execute on the server contract. Never download the entire dataset for client filtering/sorting, or compute global counts from a page. Render a bounded page with stable job-id tracking. No infinite scroll, virtual scrolling, NgRx, service worker or SSR unless evidence warrants a separately approved scope change.
- Use no polling, speculative detail prefetch, or automatic retry loops. Provide explicit refresh and retry. A configurable bounded memory cache can reduce repeat reads but cannot deliver server scalability by itself.
- Carry forward 100% unit coverage: statements, branches, functions and lines, per file for all authored executable production TypeScript including config/bootstrap where instrumentable. Include unimported source files. Only generated/dependency/declaration/test files may be excluded, with exact exclusions documented. No suppression comments, disabled tests or weakened thresholds. Tests live under apps/job-search/tests, not src.
- Test observable behavior, rendered templates, HTTP requests and races. Use deterministic injected clocks and controlled timers. Add Playwright tests in e2e with intercepted API fixtures clearly labeled as mocks.
- Native accessible controls, responsive styling consistent with the posting app, semantic links, keyboard focus and restrained live announcements. Render descriptions as text; do not assume currency or parse date-only strings through UTC conversion.
- Support useful 2xx, 4xx and 5xx outcomes, malformed bodies, network failures and timeouts. No special 401/403 behavior, auth redirects or auth tests. They may use the generic client-error path.
- Do not commit, push, publish or contact anyone without a separate user request. Maintain truthful stage work notes in ai-log/job-search-client-work-notes.md; work notes are not an exported chat transcript.
- At each stage run relevant checks and report actual results, assumptions and unresolved backend dependencies. Do not claim latency, scalability or accessibility compliance without measurements.

## Proposed query contract to finalize in stage 2

- GET /api/jobs with optional q, department, location, opaque cursor, limit and allowlisted sort. Initial default: limit 20, maximum 50; newest first with server-side createdAt/id tie-breaker. Filters are optional text matches, not dropdown facets that require fetching all jobs. Default empty query lists available jobs.
- 200 list body: { items: JobSummary[], nextCursor: string | null }. JobSummary: id, title, department, location, salaryMin, salaryMax, closingDate, createdAt. Full description is omitted from summaries. No mandatory total count. Empty items with null cursor is a valid empty result.
- GET /api/jobs/:id returns 200 with the complete saved record, including description, consistent with posting-client field names. Encode identifiers safely. Detail 404/410 means unavailable; never generate invented details from a summary.
- Proposed open-job semantics, text matching, sort order, cursor stability/expiration and limits must be documented as server obligations. The client must not silently remove jobs using its local clock or infer eventual visibility guarantees.
- 202 means not ready, not an empty list. 204 and malformed success bodies are protocol errors unless explicitly agreed otherwise. 400/422 report invalid query; 409 may report an expired cursor only when a documented machine code is present. 429 respects valid Retry-After before manual retry. Other 4xx, 5xx, network and timeout have distinct safe messages. Cancellation is not a displayed failure.
- Cache defaults are tunable assumptions: 30-second TTL, at most 50 entries shared across query/detail reads; session memory only. Key includes API scope, normalized query, cursor, limit and sort, or detail id. Explicit refresh bypasses cache. Never cache failures. Expired data must be labeled stale if displayed during revalidation. Newly posted jobs can be absent until refresh/read-model propagation; no browser-local synchronization with the posting app.

## Execution order

1. 01-angular22-search-foundation.md
2. 02-query-api-contract-and-response-handling.md
3. 03-url-driven-search-and-pagination.md
4. 04-query-cancellation-and-bounded-cache.md
5. 05-job-list-and-detail-experience.md
6. 06-full-unit-test-coverage.md
7. 07-playwright-and-performance-verification.md
8. 08-client-verification-and-documentation.md

## Official references to verify when executing

- https://angular.dev/reference/versions
- https://angular.dev/guide/signals
- https://angular.dev/guide/http
- https://angular.dev/guide/routing
- https://angular.dev/guide/testing
- https://angular.dev/guide/forms/signals/overview
- https://rxjs.dev/api/operators/switchMap
- https://playwright.dev/docs/test-intro
