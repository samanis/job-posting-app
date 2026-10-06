# Job search API

Stages 1-9 foundation, contracts, separate search PostgreSQL persistence, RabbitMQ consumption/lifecycle and read endpoints are implemented. All application source, tests, tools, configuration and prompts live here. There are no references to posting projects/source/database/HTTP endpoints. DI registration opens no connection; the enabled hosted consumer connects asynchronously at startup. Projection operations use only the separate search database. GET /api/jobs provides available listings with signed keyset pagination; GET /api/jobs/{id} returns full details, including closed jobs. Dependency-free liveness and separate DB read readiness/broker ingestion health are implemented.

## Build, verify and run

From the repository root:

```sh
cd API/job-search.api
dotnet restore JobSearch.slnx --locked-mode
dotnet build JobSearch.slnx --configuration Release --no-restore
dotnet run --project tools/CoverageGate
dotnet run --project src/JobSearch.Api --launch-profile http
```

Development listens on http://localhost:5100 and serves /openapi/v1.json (including both read routes). Production/Staging do not expose OpenAPI or test endpoints. Test-only fault controllers belong to the test assembly and are registered only by an explicitly opted-in test factory.

.NET SDK10.0.101 is pinned with latestPatch roll-forward. NuGet.Config explicitly selects the official source; all projects have package locks and warnings are errors. Versions are a verified baseline, not a claim of latest servicing. Real secrets go in environment variables or secret configuration, never committed files. Supply search database credentials externally for projection/migrations; configure the existing broker credentials externally; production cursor signing keys must also be configured externally (see docs/search.md).

Search:MaximumEventBodyBytes defaults to65536 and validates1..1048576 at startup; the ingestion reader enforces it. Environment override: Search__MaximumEventBodyBytes. TimeProvider.System is injected and replaceable in tests. Host shutdown timeout is30s; consumer drain defaults to24 seconds with6 seconds reserved for resource cleanup.

## Diagnostics and verification

Central IExceptionHandler returns generic application/problem+json500 with traceId and X-Trace-Id, including a JSON fallback for callers requesting HTML. The response never includes raw exception details. Structured UTC JSON application logs contain safe failure type, generated trace ID, method, matched route template, status and duration; not raw path/query/header/body or exception stacks. ASP.NET framework logs are suppressed to avoid duplicate unsafe request/exception detail. Application logging remains enabled. Any later driver logging needs an explicit safe policy.

Isolated unit coverage and host bootstrap coverage are separate exact100% line/branch/method gates. Authored source and declared types are checked using the SDK Roslyn parser. Unit excludes Program only; host covers Program separately. Generated obj/bin output is excluded; coverage-exclusions.json contains two hash-checked unmodified EF designer/snapshot files; authored migration Up/Down remains covered. Negative checks reject missing/empty reports, uncovered line/branch/method and omitted source/type/module. Reports under tests/JobSearch.Api.Tests/TestResults are ignored. These checks cannot establish broker/database correctness; external PostgreSQL integration evidence is separate from the coverage score.

[Ordered prompts](prompt/README.md). The selected independent API stages are complete; see [final verification](docs/final-verification.md), [design](docs/design.md), [runbook](docs/runbook.md), and [client integration handoff](docs/client-integration-handoff.md). Frontend integration and full exercise submission remain separate work.

Official references: [ASP.NET Core error handling](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/error-handling?view=aspnetcore-10.0), [Coverlet package](https://www.nuget.org/packages/coverlet.msbuild/10.0.1).

See [event/query contracts](docs/contracts.md) for ingestion validation, canonical identity, query boundaries and implemented HTTP serialization.

See [search persistence](docs/persistence.md) for explicit setup/migrations, write concurrency, commit uncertainty and real PostgreSQL verification.

See [consumer lifecycle and quarantine](docs/messaging.md). Migrate the separate search database before enabling ingestion. Stage5 provides confirmed quarantine, bounded reconnect/drain and separate read/ingestion health.

See [search endpoints and signing-key setup](docs/search.md) for filters, cursor expiry/rotation, UTC availability and errors. Development uses an explicitly nonproduction key only when no keys are configured; Production/Staging refuse missing keys or that development key.

See [measured read performance and operational checks](docs/performance/README.md). The bounded tools/ReadWorkload runner owns disposable resources and records actual HTTP percentiles and PostgreSQL plans; its results are local observations, not production guarantees.

See [Docker and host setup](docs/docker.md): independent non-root images, explicit migrations, separate persistent search PostgreSQL and existing external RabbitMQ. Run tools/Verify-Compose.ps1 for isolated clean-checkout verification; normal Compose never starts another broker.

