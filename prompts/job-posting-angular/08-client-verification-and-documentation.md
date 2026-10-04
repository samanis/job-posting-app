# Stage 8: Client verification and documentation

Read `prompts/job-posting-angular/shared-requirements.md` first. Stages 1–7 must exist; implement only this stage.

## Tasks

1. Audit the final client against every exercise requirement and the user's decisions. Fix concrete client defects with accompanying tests; do not start backend work.
2. Write/update `apps/job-posting/docs/local-development.md` with verified prerequisites, install/start/build/test/coverage commands, proxy setup, routes, and source organization. Explain how to run against a future API and that a backend is not delivered by these stages.
3. Finalize `docs/api-contract.md` with status handling, validation formats, closing-date semantics, salaries, Idempotency-Key lifecycle, conflict codes, replay behavior, and retention requirements. List unresolved backend agreements explicitly.
4. Explain client duplicate protection scope: concurrent clicks and retries in one tab, refresh recovery, and uncertain outcomes. State the backend atomic idempotency requirement without claiming it has been implemented.
5. Document Angular 22 features actually used and their purpose, accessible UI behavior, coverage scope/exclusions, and the limits of unit tests.
6. Update truthful work notes. Explain how to add an actual chat transcript under `ai-log`; do not substitute generated notes for the required transcript or fabricate conversation history.

## Final checks

- Run production build, all unit tests, and enforced 100% coverage. Include exact results.
- Use HttpClient test fixtures to verify a successful post and validation/unknown-outcome/retry flows without implementing a mock API server.
- Review modified file scope: no search app, API, database, Docker, authentication, or authorization changes.
- Report client completion separately from full exercise completion. Full submission still needs both APIs, search app, persistence/migration, Docker Compose, genuine AI transcript, and repository submission.
- Do not execute later work, publish, push, or contact the recruiter.
