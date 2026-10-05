# Job posting API application

All source and supporting files for the job posting API live together here:

```text
job-posting-api/
  src/JobPosting.Api/
  src/JobBoard.Persistence/
  tests/JobPosting.Api.Tests/
  tools/CoverageGate/
  ai-log/
  JobBoard.slnx
  global.json
  Directory.Build.props
  NuGet.Config
  01-dotnet10-foundation.md ... 09-integration-verification-and-handoff.md
```

`src/JobPosting.Api` is the C# project within this application. Source, persistence, tests, tools, prompts and configuration belong to the same application root. Frontends and the unimplemented search API remain separate.

From the repository root:

```sh
cd API/job-posting-api
dotnet restore JobBoard.slnx --locked-mode
dotnet build JobBoard.slnx --no-restore
dotnet run --project tools/CoverageGate
dotnet run --project src/JobPosting.Api --launch-profile http
```

Run backend commands from this directory so its SDK/configuration apply. See the [service guide](src/JobPosting.Api/README.md), [API contract](src/JobPosting.Api/docs/api-contract.md) and [prompt index](PROMPTS.md).

Stages 1–2 are implemented. POST `/api/jobs` and persistence/messaging infrastructure remain later-stage work. Future Dockerfiles, Compose, environment examples and database/broker settings must live inside this application folder too. `src/JobBoard.Persistence` remains an empty placeholder; no shared database implementation was added.
