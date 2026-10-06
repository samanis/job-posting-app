# Job search app

Angular 22 app where job seekers browse open positions, filter and sort them, page through results, and open a job to see its full details. It reads from the [Job Search API](../../API/job-search.api), so jobs posted in the Job Posting app appear here within a few seconds. The filters and page are kept in the URL, so links and the back button work.

Requires Node.js 22.22.3 or later in the Node 22 series and npm. For the whole system, see the [root README](../../README.md).

The UI uses Angular Material 22 with a Material 3 azure theme in `src/styles.scss`, matching the posting app. Fonts use the local system stack; no external font or icon service is needed.

## Run locally

Run these commands from the repository root:

```powershell
npm.cmd --prefix .\apps\job-search ci
npm.cmd --prefix .\apps\job-search start
```

Keep the terminal running and open http://localhost:4201/jobs. Stop the server with **Ctrl+C**. On macOS/Linux, use `npm` instead of `npm.cmd`.

To see live jobs, start the backend first with `docker compose up --build` from the repository root. The development proxy forwards `/api/**` to that search API at http://localhost:5101. To use a different origin (for example, the API running on the host at port 5100), set it before starting:

```powershell
$env:JOB_SEARCH_API_URL = 'http://localhost:5100'
npm.cmd --prefix .\apps\job-search start
```

If installation reports `EPERM` for `esbuild.exe`, stop running development servers before retrying `ci`. Use the `--prefix` commands above to avoid missing `package.json` errors at the repository root.

## Test and build

From the repository root:

```powershell
npm.cmd --prefix .\apps\job-search run test:coverage
npm.cmd --prefix .\apps\job-search run build
```

Unit tests enforce 100% statements, branches, functions, and lines for each executable production TypeScript file. Build output is `apps/job-search/dist/job-search/browser/`.

## Browser tests (Playwright)

```powershell
npm.cmd --prefix .\apps\job-search run test:e2e:install
npm.cmd --prefix .\apps\job-search run test:e2e
```

Run from the repository root. Tests start their own server on port 4302 and use mocked API responses; no backend is required. Keep port 4302 free. Use `test:e2e:report` to open the report or `test:e2e:headed` to watch the browser.

## Deploy

Upload the production build's `browser/` contents to a static host. Configure frontend routes (`/jobs` and `/jobs/:id`) to fall back to `index.html`, and route `/api/**` to the deployed search API before applying that fallback. `JOB_SEARCH_API_URL` configures local development only.

