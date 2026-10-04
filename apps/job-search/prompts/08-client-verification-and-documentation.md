# Stage 8: Final client audit and local instructions

Read apps/job-search/prompts/shared-requirements.md. Stages 1-7 must be complete. Implement only this stage.

1. Audit required available listings/full details against the exercise and each chosen query/cache behavior against implemented code. Confirm independent Angular 22 project and no backend/auth implementation. Clearly separate confirmed client behavior from outstanding server contracts.
2. Write a short apps/job-search/README.md with prerequisites, exact commands from monorepo root using npm.cmd --prefix .\apps\job-search, clean installation, local run/URL, configurable API proxy target, production build/output, unit coverage and Playwright commands. Explain stopping dev servers before npm ci on Windows. Include static hosting/deep-route fallback and deployment API-origin configuration; no deployment execution.
3. Finalize docs/api-contract.md, docs/query-performance.md and docs/testing-and-coverage.md: endpoints, response schemas, filters, cursor semantics, URL behavior, cancellation, cache bounds/TTL/in-flight policy, refresh/eventual consistency, error handling, build budgets and measured verification.
4. Without a backend, document that normal live runs need a compatible API and that browser tests use interception. Do not ship fake jobs as the production default. Enumerate outstanding inter-app/API integration, backend indexing/read-model/cache decisions and real load-test needs.
5. Verify lockfile installation, production build, unit coverage, E2E type check and browser tests using documented commands. Check docs match actual ports/output and git diff --check. Keep genuine AI transcript instructions distinct from work notes.

Acceptance: short accurate README, finalized client docs, all actual gates pass and limitations stated. Report final files/results. Do not automatically commit, push, implement backend or execute another prompt.
