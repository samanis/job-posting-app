# Stage 1: Angular 22 search foundation

Read apps/job-search/prompts/shared-requirements.md. Implement only this stage.

1. Inspect the repository and posting app's compatible toolchain/testing conventions. Scaffold an independent Angular 22 project in apps/job-search without deleting/overwriting prompts. Verify stable versions and Node compatibility using official documentation.
2. Configure standalone bootstrap, zoneless change detection, strict checks, OnPush, HttpClient, /jobs list and lazy /jobs/:id detail routes, root redirect and accessible not-found page. Use a separate dev port from posting, proposed 4201. Avoid premature framework abstractions.
3. Create a small responsive shell with skip link, main landmark and route title/focus plan. Placeholder pages must explicitly avoid fabricated job data.
4. Configure Angular-supported Vitest tests under tests, mirroring source paths. Set production source inclusion and the eventual 100% per-file coverage gate. Keep build budgets explicit and record them.
5. Add local development proxy configuration with configurable search API target; this does not create an API. Add useful npm scripts and ignore generated output.

Acceptance: install from lockfile, build and foundation tests pass; direct list/detail route tests and unknown route behavior pass; no posting app edits or backend implementation. Record exact versions and commands.
