# Job posting app

Angular 22 app where a hiring manager posts a new job opening. It validates the form on the client, submits it to the [Job Posting API](../../API/job-posting-api) with an idempotency key so retries can't create duplicates, shows the API's field-level validation errors on the form, and on success shows the saved record exactly as the API returned it.

Requires Node.js 22.22.3 or later in the Node 22 series and npm. For the whole system, see the [root README](../../README.md).

The UI uses Angular Material 22 with a Material 3 azure theme in `src/styles.scss`. Fonts use the local system stack; no external font or icon service is needed.

## Run locally

From the repository root:

```powershell
npm.cmd --prefix .\apps\job-posting ci
npm.cmd --prefix .\apps\job-posting start
```

Keep the terminal running and open the URL printed by Angular (usually http://localhost:4200). On macOS/Linux, use `npm` instead of `npm.cmd`.

To save jobs, start the backend first with `docker compose up --build` from the repository root. The proxy defaults to that posting API at http://localhost:5000. To use a different origin, set it before starting:

```powershell
$env:JOB_POSTING_API_URL = 'http://localhost:5000'
npm.cmd --prefix .\apps\job-posting start
```

The development proxy forwards `/api/**` to that API. Without a backend, the form runs but cannot save jobs.

If installation reports `EPERM` for `esbuild.exe`, stop any running Angular development server with **Ctrl+C** before running `ci` again. Installation replaces dependencies and must not run while the server is using them. If you see `ENOENT` for the repository root's `package.json`, use the `--prefix` commands above.

## Test and build

From `apps/job-posting`:

```powershell
npm.cmd run test:coverage
npm.cmd run build
```

Tests enforce 100% coverage for production TypeScript. The production build is written to `dist/job-posting/browser/`.

## Deploy

1. Run the tests and production build above.
2. Upload the contents of `dist/job-posting/browser/` to your static web host.
3. Configure the host to serve `index.html` for frontend routes such as `/jobs/new`.
4. Configure a reverse proxy for `/api/**` to the deployed job posting API, preserving the path and `Idempotency-Key` header. API requests must reach the backend rather than the frontend fallback.

`JOB_POSTING_API_URL` configures local development only. The client expects the [API contract](docs/api-contract.md), including atomic idempotency, which the Job Posting API implements. The API is deployed separately.

## Browser E2E tests (Playwright)

From the repository root:

```powershell
npm.cmd --prefix .\apps\job-posting run test:e2e:install
npm.cmd --prefix .\apps\job-posting run test:e2e
```

The suite starts/stops its own Angular server on port 4300 and runs desktop/mobile Chromium with mocked API responses. No backend is required. Keep port 4300 free. Run `test:e2e:headed` to see the browser, `test:e2e:report` to open the HTML report, or `test:e2e:check` to type-check the tests. E2E tests complement the separate 100% unit coverage gate.

