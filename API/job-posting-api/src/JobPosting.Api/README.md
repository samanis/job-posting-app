# Job posting API

Stages 1–2 of an ASP.NET Core .NET 10 service. It provides configuration validation, structured request/exception logs, safe Problem Details, development-only OpenAPI, create/saved/accepted contracts, strict JSON parsing, independent field/date validation and versioned request fingerprints. See the [API contract](docs/api-contract.md) for exact rules, response examples and the deferred Angular 202 integration. **POST `/api/jobs` is not implemented and returns 404.** PostgreSQL, migrations, durable idempotency, RabbitMQ, circuit breaking, health checks and Docker come in subsequent stages.

## Build and test

Install the .NET SDK selected by `API/job-posting-api/global.json` (10.0.101, with patch roll-forward within the 10.0.1xx feature band). Change into `API/job-posting-api` before running the commands below. Package lockfiles preserve the dependency graph. No global tool, database or broker is required at this stage.

```sh
dotnet restore JobBoard.slnx --locked-mode
dotnet build JobBoard.slnx --no-restore
dotnet test JobBoard.slnx --no-build --no-restore
dotnet run --project tools/CoverageGate
```

ASP.NET OpenAPI/testing packages are pinned to 10.0.1 to match the installed 10.0.1 runtime; this is a tested baseline, not a claim to use the latest servicing release. Upgrade the SDK/runtime and corresponding package pins together when refreshing the baseline, then regenerate lockfiles and rerun the tests. Restore uses the repository NuGet.Config and requires access to nuget.org on a fresh machine.

`Microsoft.OpenApi` is explicitly pinned to patched version 2.7.5 because the upstream 10.0.1 package's minimum dependency resolves to vulnerable 2.0.0. NuGet auditing remains enabled and warnings fail the build. See the [upstream advisory](https://github.com/microsoft/OpenAPI.NET/security/advisories/GHSA-v5pm-xwqc-g5wc).

## Mandatory coverage gate

Run `dotnet run --project tools/CoverageGate` from `API/job-posting-api` on Windows, macOS or Linux. The pinned .NET SDK is the only prerequisite; the checker uses the C# parser bundled with that SDK. This is also the developer/CI verification command. A normal `dotnet test` run alone does not enforce coverage.

The command performs locked solution restore, then separately instruments isolated `Category=Unit` tests and in-process `Category=Host` tests using Coverlet MSBuild 10.0.1. Both suites must reach 100% lines, branches and instrumented methods. There is no separately measured statement metric. Unit coverage includes every concrete authored type in configuration, diagnostics, contracts and validation; host coverage covers only `src/JobPosting.Api/Program.cs` startup/registration code. External dependency integration tests cannot contribute to either score. Additional business/adapters belong in the isolated unit scope as later stages are implemented.

Coverlet enforces 100% for each production assembly. The checker additionally rejects any uncovered sequence point or branch, including within individual files/types, and uses parsed production sources to require every concrete declared type and authored C# file. Missing/empty reports or assemblies fail. It deletes the known previous report before each run and rejects coverage-suppression attributes. Negative checks run automatically using temporary copies of reports: missing/empty report, uncovered line/branch/method and missing source/type/assembly must all be rejected. No thresholds are lowered.

Reports (generated and Git-ignored):

- `tests/JobPosting.Api.Tests/TestResults/unit/coverage.json`
- `tests/JobPosting.Api.Tests/TestResults/host/coverage.json`

Current Stage 2 measurements: unit suite 121 passing cases, lines 325/325, branches 120/120, methods 64/64 across 17 authored files/types; host suite 13 passing cases, lines 41/41, branches 4/4, methods 1/1 in Program.cs. Coverlet consolidates compiler-generated methods/state machines into its reported entries; these are instrumenter counts, not counts of C# method declarations. Every authored production file is included across the two separate scopes.

Exact exclusions and reasons:

- The unit scope assigns only `Program.cs` to the separate host gate because executing application bootstrap requires an in-process host; it is still required and must reach 100%.
- Test assemblies and dependencies are outside the posting production assembly filters `[JobPosting.*]*,[JobBoard.Persistence]*`. All production projects under src are checked; Program bootstrap is assigned to the separate host gate.
- `**/obj/**/OpenApiXmlCommentSupport.generated.cs` is unmodified output from Microsoft's OpenAPI source generator, not authored service logic. No other source-file exclusion is configured. `bin`/`obj` are generated build outputs and are excluded from the source inventory.
- `ITimeZoneResolver` consists only of an interface declaration and has no executable sequence points; its concrete `SystemTimeZoneResolver` is covered. Declaration-only interfaces/enums have no executable coverage metric. Compiler-generated code implementing authored logic remains included.
- `tools/CoverageGate` is development verification tooling, outside the production API/service scope.

To check an existing report independently: `dotnet run --project tools/CoverageGate -- --verify Unit tests/JobPosting.Api.Tests/TestResults/unit/coverage.json` (or `Host` and its report). Use the full command above for fresh, reproducible acceptance.

## Run locally

```sh
dotnet run --project src/JobPosting.Api --launch-profile http
```

The Development profile listens on http://localhost:5000. Its document is at http://localhost:5000/openapi/v1.json; the document has no job operation yet. No Swagger UI is installed. Stop with Ctrl+C. This host/port matches the posting client's documented proxy example; it cannot save jobs until later API stages are implemented.

Production-like local run (no development documentation):

```sh
dotnet run --project src/JobPosting.Api --no-launch-profile -- --environment Production --urls http://localhost:5000
```

## Configuration and diagnostics

`appsettings.json` contains safe defaults. Override through normal ASP.NET configuration, for example `JobPosting__BusinessTimeZone=UTC` or `JobPosting__MaximumRequestBodyBytes=32768`. Both are validated at startup. Default timezone is America/Toronto; request size is bounded to 65536 bytes, configurable from 1 to 1048576 bytes. TimeProvider and the configured TimeZoneInfo are injectable and used by the independent new-request closing-date validator.

One JSON console logger configuration includes UTC timestamps and trace scopes. Every completed request logs method, matched route template, status, elapsed time and trace identifier. Routine request logs exclude body, query string, arbitrary URL and raw idempotency headers. Unexpected exceptions are logged with full diagnostic details server-side; restrict access to those logs. Production HTTP responses expose only generic Problem Details and `traceId`; `X-Trace-Id` supplies the same identifier. No developer exception page or test fault endpoint is deployed.

## Source boundaries

- `Configuration`: typed startup settings and validation.
- `Diagnostics`: shared request logging and unexpected-error handling.
- `Endpoints`: future create-job HTTP operations.
- `Contracts`: create, immutable normalized payload, saved/accepted response and Problem Details contracts; future event contracts.
- `Validation`: strict JSON parsing, normalization/field validation, versioned fingerprints, opaque header validation and separately applied new-request date validation.
- `Application`: future transaction/publication coordination.
- `Persistence`: future posting-owned EF Core/PostgreSQL implementation.
- `Messaging`: future broker abstraction and RabbitMQ adapter.

Only implemented boundaries contain C# today. Future boundaries are documented here instead of introducing placeholder classes. Tests under `tests/JobPosting.Api.Tests` separate isolated configuration/diagnostics tests from WebApplicationFactory host tests with opt-in test-only controllers; they do not simulate completed business persistence. API stage prompts are in [implementation prompts](../../PROMPTS.md).
