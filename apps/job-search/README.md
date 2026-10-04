# Job search app

Angular 22 client. Requires Node.js 22.22.3 or later in the Node 22 series and npm.

## Run locally

Run these commands from the repository root:

```powershell
npm.cmd --prefix .\apps\job-search ci
npm.cmd --prefix .\apps\job-search start
```

Keep the terminal running and open http://localhost:4201/jobs. Stop the server with **Ctrl+C**. On macOS/Linux, use `npm` instead of `npm.cmd`.

To connect a running search API, set its origin before starting (replace the example address):

```powershell
$env:JOB_SEARCH_API_URL = 'http://localhost:5001'
npm.cmd --prefix .\apps\job-search start
```

The development proxy forwards `/api/**` to that API. No backend is implemented yet; displaying live jobs requires an API matching the [query contract](docs/api-contract.md).

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
