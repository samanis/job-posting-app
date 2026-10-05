# Job posting and search clients

Two independent Angular applications live in `apps/job-posting` and `apps/job-search`. Each app has its own package manifest, committed npm lockfile, source, tests, and build configuration.

Backend applications, tests, .NET build configuration and infrastructure belong under `API/`, separately from the Angular applications in `apps/`. All job posting API source, persistence, tests, tools, prompts and configuration live together in `API/job-posting-api`; see its [application guide](API/job-posting-api/README.md). Job creation and backend dependencies are not implemented yet.

## Prerequisites

- Node.js 22.22.3 or later in the Node 22 series. Check with `node --version`.
- npm (the project uses npm 10.9.8). Check with `npm --version`.
- Internet access for the initial dependency installation.

No global Angular CLI installation is needed. Run the following commands from the repository root after cloning or pulling the repository.

## Install and run

```sh
npm --prefix apps/job-posting ci
npm --prefix apps/job-search ci
```

Start each app in a separate terminal:

```sh
npm --prefix apps/job-posting start
```

Open http://localhost:4200/jobs/new.

```sh
npm --prefix apps/job-search start
```

Open http://localhost:4201/jobs. Stop either server with **Ctrl+C**. On Windows PowerShell, use `npm.cmd` if execution policy blocks `npm`.

The clients start without environment files or API credentials. Backend APIs are not implemented in this repository: saving jobs and loading live search results require compatible APIs. To connect them, follow the app guides for `JOB_POSTING_API_URL` and `JOB_SEARCH_API_URL`:

- [Job posting setup and API contract](apps/job-posting/README.md)
- [Job search setup and API contract](apps/job-search/README.md)

After pulling changes to a lockfile, stop the affected development server and rerun that app's `npm ci` command.

## Verify changes

```sh
npm --prefix apps/job-posting run test:coverage
npm --prefix apps/job-posting run build
npm --prefix apps/job-search run test:coverage
npm --prefix apps/job-search run build
```

For browser tests, install Chromium once per app, then run its suite. The suites use mocked APIs and start their own development servers:

```sh
npm --prefix apps/job-posting run test:e2e:install
npm --prefix apps/job-posting run test:e2e
npm --prefix apps/job-search run test:e2e:install
npm --prefix apps/job-search run test:e2e
```

## Repository files

Commit source files, assets, tests, `package.json`, `package-lock.json`, Angular/TypeScript/proxy/Playwright configuration, documentation, and app prompts. Prompts live in each app's `prompts/` folder.

Git ignores installed dependencies, generated builds, caches, coverage, test reports, and local editor/system artifacts. These are recreated by the commands above and are not needed in a checkout. There is no root npm package; use the app-specific `--prefix` commands.
