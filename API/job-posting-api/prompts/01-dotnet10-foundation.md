# Stage 1: .NET 10 service foundation

Read `API/job-posting-api/prompts/shared-requirements.md` and `requirements-review.md` first. Inspect completed stages and applicable AGENTS.md. Implement only this stage; earlier numbered stages must be complete. Preserve unrelated work.

## Tasks

1. Inspect the API skeleton/configuration, existing client contracts and local dotnet/Docker versions. Preserve all unrelated Angular changes. Establish a small standalone ASP.NET Core net10.0 service at API/job-posting-api/src/JobPosting.Api and posting test project(s) under API/job-posting-api/tests. Keep the solution, SDK/tool configuration and all backend infrastructure under API/job-posting-api, independently from the frontend. Run .NET commands from API/job-posting-api.
2. Configure nullable reference types, deterministic builds, dependency injection, strongly typed validated options and a testable TimeProvider. Check stable package compatibility before adding dependencies; do not downgrade .NET.
3. Establish Endpoints, Contracts, Validation, Persistence, Messaging and application coordination boundaries using folders; introduce separate projects only with a concrete reason.
4. Add basic centralized structured ILogger configuration, trace IDs, production-safe exception handler/Problem Details and development OpenAPI using supported .NET 10 APIs. No developer exception page or diagnostics endpoint in Production.
5. Add focused foundation tests through WebApplicationFactory, configuration failure tests and a sanitized unexpected-error test. No fake success POST or placeholder business persistence.
6. Record actual commands and prepare required Git ignore rules for bin/obj/test outputs without excluding migrations, source, configuration or safe examples.
7. Add the mandatory coverage setup and failing 100% line/branch/method gate from shared-requirements.md. If this stage's foundation already exists, retrofit coverage and missing behavior tests without regenerating it. Distinguish isolated unit tests from in-process host tests, include authored bootstrap/diagnostics/configuration code, and keep generated reports ignored while tracking required configuration/tool files.

## Acceptance and checks

- dotnet restore, dotnet build and the foundation tests run successfully.
- The mandatory 100% coverage gate passes with complete authored-source inclusion and measured reports; the previous test count alone is not evidence of coverage.
- Production unexpected failures return generic Problem Details and a traceId, without stack/config details.
- Project/SDK/tool files are reproducible; no search service, Angular change or business stub pretending to accept a job.

Run appropriate build/tests and record actual results in `API/job-posting-api/ai-log/job-posting-api-work-notes.md`. Report changed files, assumptions and blockers. Do not commit, push or continue into later stages automatically.

