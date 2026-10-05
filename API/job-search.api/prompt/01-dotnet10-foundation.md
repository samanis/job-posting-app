# Foundation and isolated application

Read shared-requirements.md and requirements-review.md in this prompt directory first. Apply every shared constraint. Execute only this stage after previous stages have passed; preserve unrelated work.

## Tasks and acceptance

Create independent .NET10 Web API solution/project under src/JobSearch.Api, unit/host tests, coverage tool, SDK/package locks and application-local configuration/ignore files. Use feature folders and minimal abstractions with no cross-application references. Add central safe structured logging, trace propagation and generic production ProblemDetails, typed startup-validated config, injectable TimeProvider. Establish deterministic unit and host coverage gates with complete-source/type and intentional negative checks. Create application README and work notes. No DB/broker connection at registration and no fake successful search endpoint.

Acceptance: locked restore/build, valid/invalid startup and safe exception/trace tests, all coverage gates100%; independently buildable without posting source. No generated outputs/secrets ignored incorrectly.

Run appropriate build/tests and mandatory exact 100% coverage gates. Write actual commands/results and remaining limitations in API/job-search.api/ai-log/job-search-api-work-notes.md. Do not proceed to the next stage automatically.
