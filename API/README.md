# Backend applications

Each backend application owns its source, persistence, tests, tools, configuration and infrastructure within its own folder.

- [job-posting-api](job-posting-api/README.md): implemented .NET 10 stages 1-2, including all posting tests/tools/prompts and SDK/build settings.
- `JobSearch.Api/`: existing unimplemented search placeholder, outside the posting application.

Frontend applications remain independent under `../apps/`. Use the [posting application guide](job-posting-api/README.md) for build, run and verification commands.
