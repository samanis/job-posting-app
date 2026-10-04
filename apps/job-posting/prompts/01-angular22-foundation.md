# Stage 1: Angular 22 foundation

Read `apps/job-posting/prompts/shared-requirements.md` and follow its shared instructions. Implement only this stage.

## Tasks

1. Inspect the empty/existing `apps/job-posting` tree and local Node/npm versions. Verify Angular 22 compatibility from official documentation.
2. Scaffold one Angular 22 application directly in `apps/job-posting`, without nested duplicate folders. Keep package and lock files in this app. Use routing, strict checking, standalone components, zoneless behavior, and local CSS.
3. Configure a lazy `/jobs/new` route and a minimal accessible application shell. Redirect the root route to the form route and provide a simple not-found route. Do not implement the form yet.
4. Establish the existing `core/api`, `features/job-posting/components`, `models`, and `validators` boundaries. Add a feature-local state folder only when later work needs it. Keep specs under apps/job-posting/tests, mirroring the source layout.
5. Set up supported Vitest unit testing and coverage tooling. Add scripts for development, production build, non-watch tests, and coverage. Establish the eventual 100% coverage policy; do not hide uncovered sources.
6. Add a development proxy/configuration seam for `/api` without hardcoding a deployed backend address. Explain that the actual API is not implemented.

## Acceptance and checks

- Angular dependencies have major version 22; no Zone.js requirement or NgModule architecture.
- Lazy route and shell work; unknown routes have intentional behavior.
- Shell and routing have meaningful tests; execute tests and production build.
- No API, search app, database, or full form code is added.
- Record actual setup commands and dependency versions for later documentation.
