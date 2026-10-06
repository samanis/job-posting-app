# Job Posting app: local development

The Job Posting app is the Angular 22 form that hiring managers use to post a job. For the whole system, including Docker and the APIs, see the [root README](../../../README.md).

## Install and run

You need Node.js 22.22.3 or later in the 22.x series. Start the backend first with `docker compose up --build` from the repository root. Without it, the form works but cannot save jobs.

```sh
npm --prefix apps/job-posting ci
npm --prefix apps/job-posting start
```

Open http://localhost:4200. The app redirects to the form at `/jobs/new`. On Windows PowerShell, use `npm.cmd` if the execution policy blocks `npm`.

## Connecting to the API

The dev server forwards every `/api/**` request to the API (see `proxy.conf.cjs`). So no CORS setup is needed. The target comes from `JOB_POSTING_API_URL` and defaults to `http://localhost:5000`. Restart the dev server after changing it.

The proxy exists only in development. In production, `/api/jobs` must be served from the same origin, for example by a reverse proxy.

## Test and build

Run from `apps/job-posting`:

| Command | What it does |
|---|---|
| `npm test` | Runs the unit tests once |
| `npm run test:coverage` | Runs the unit tests and enforces 100% coverage |
| `npm run test:e2e:install` | Installs Chromium for Playwright (once) |
| `npm run test:e2e` | Runs the Playwright browser tests |
| `npm run build` | Builds to `dist/job-posting/browser` |

- **Unit tests** run on Vitest and live in `tests/`, which mirrors `src/`. HTTP calls are faked, so no API is needed. Every file under `src/` must reach 100% statement, branch, function and line coverage. The report is in `coverage/job-posting`.
- **Browser tests** use Playwright. It starts its own dev server on port 4300 and fakes every API response. Tests run in desktop Chrome and an emulated Pixel 7 phone. They cover validation, confirmation, server errors, retries, double-clicks, throttling and layout.
- **Full-stack check (optional).** With the backend and both apps running, run `node apps/job-posting/tools/verify-local-integration.cjs`. It posts a real job, then opens it in the Job Search app. The job stays in the database.

## Source layout

| Folder | Contents |
|---|---|
| `src/app/core/api` | API types, the HTTP call and response handling |
| `src/app/features/job-posting/components` | The form page and the confirmation panel |
| `src/app/features/job-posting/models`, `validators` | The form data and validation rules |
| `src/app/features/job-posting/state` | The submission workflow and its stored record |
| `tests`, `e2e` | Unit tests and Playwright tests |
| `prompts` | The numbered prompts used to build the app |

## Main behaviours

**Angular.** Components are standalone and use signals, values that tell Angular when they change. The app is zoneless: Angular redraws only when a signal changes, not after every browser event. The form uses Signal Forms, Angular's new signal-based form API. Angular Material supplies the fields, buttons and cards.

**Validation.** The form checks each rule before sending:

- Every field is required. Spaces alone do not count.
- Salaries are non-negative, with at most two decimal places. No currency is assumed.
- The minimum salary must be less than the maximum.
- The closing date must be after today in the browser's local date.

If the API returns `400` or `422`, each error appears next to its field.

**Confirmation.** After a save, the form is replaced by the job exactly as the API returned it. This includes its ID and saved time in UTC. "Post another job" clears the form.

**Accessibility.** Fields have labels, help text and linked errors. An invalid submit moves focus to the first invalid field. A polite live region announces the submission status without interrupting. Focus moves to the confirmation after a save, and back to the title field for the next job.

**Duplicate protection.** Each submission carries a unique idempotency key, kept in sessionStorage. Retries reuse it, so the API saves the job only once. See [client-idempotency.md](client-idempotency.md).

## AI transcripts

The AI chat transcripts and work notes are in [ai-log](../../../ai-log/README.md).
