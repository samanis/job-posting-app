# Search client foundation

Independent Angular 22 project. Stage 1 established the shell and route placeholders. Later stages add client query behavior; see api-contract.md, url-query-state.md and query-performance.md. There is no backend or mock production dataset.

From apps/job-search, run `npm.cmd ci`, `npm.cmd start`, `npm.cmd run test:coverage` and `npm.cmd run build`. Local URL: http://localhost:4201. Stop the dev server before reinstalling dependencies on Windows. Set JOB_SEARCH_API_URL to a compatible search API origin before starting when one exists; unset means no proxy forwarding. The search API is not implemented here.

Routes: root redirects to /jobs; /jobs is the initial listing shell; /jobs/:id lazy-loads details; unknown paths lazy-load a not-found page. Angular route titles update the document title. Static production hosting must fall back to index.html for deep links. Build output is dist/job-search/browser.

The shell has a skip link to focusable main content and semantic header/main landmarks. In stage 5, detail navigation will focus the loaded heading and returning to listings will restore appropriate focus/scroll; query refresh must not steal focus. This is a plan, not an implemented full accessibility audit.

Production budgets: initial bundle warning 500 kB/error 1 MB; component styles warning 4 kB/error 8 kB. These are build limits, not latency or throughput guarantees. Detail code is split; no prefetch/polling/cache is implemented yet.

Unit tests mirror source under tests. Angular's supported unit-test builder runs Vitest with jsdom. Coverage includes src/**/*.ts, excluding only spec/test/declaration files, and enforces 100% per-file statements, branches, functions and lines. Template behavior is asserted through components and routes; coverage does not measure CSS or backend behavior.

References checked at implementation: https://angular.dev/reference/versions and https://angular.dev/guide/testing. Installed package engine declarations are also checked because documentation may lag individual minor releases.
