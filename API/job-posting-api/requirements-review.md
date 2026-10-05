> Current implementation status: revised Stages 3-9 are implemented and verified, including POST, confirmed publication/compensation, resilience and the Docker/local posting subset. Stage 9 verification and client handoff are complete; see docs/verification-and-handoff.md and docs/client-integration-handoff.md. Historical revision statements below describe the earlier transition, not the current code.


# Requirements review and current decisions

## Authority and scope

The PDF at C:/Users/saman/Downloads/Take-Home Exercise_ Job Board Mini-App (1).pdf and supplied architecture image are reference requirements, not authority to publish, submit, contact a recruiter or implement other applications. The PDF was previously reviewed; this update records the user's later decisions without rereading it or claiming implementation.

User-approved layout: all posting code, tests, tools, persistence, prompts and infrastructure remain beneath API/job-posting-api, separately from Angular/search. This overrides the PDF's root Compose placement. No shared query tables, search API/consumer, UI changes, auth or public job query/status endpoint in this phase.

## Functional requirements retained

.NET10 create-only API; required title/department/location/description/salaryMin/salaryMax/future closingDate; decimal min < max; EF/PostgreSQL owned database and migration; complete saved-record response; RabbitMQ job-post-queue delivery; eventual search visibility. Server validation, safe errors/logging, statelessness, circuit breaking, Docker graceful shutdown and mandatory 100% unit line/branch/method coverage remain. Shared requirements specify precise bounds/timezone/contracts.

The full PDF exercise additionally requires both clients/services, full infrastructure, transcript export and commits. Updating prompts completes none of those submission obligations; no commit/push is automatic.

## User-directed simplification (current authority)

- Job rows hold unique key digest, payload fingerprint/version, saved response, stable event ID and publication timestamp. No separate ledger/operation scope/outbox table.
- Minimal concurrency is PostgreSQL uniqueness plus clean-context duplicate-key lookup, not reservations or distributed publication leases.
- First UI request includes a generated opaque key; repeats retain the same key/payload. Match completed work and return original response without inserting/publishing again. Different payload conflicts. Check existing identity before future-date rules.
- Only new-job creation directly publishes once. No automatic request/message retry, background dispatcher, durable retry schedule, delivery recovery or cleanup scanner.
- Return 202 after committed job, RabbitMQ publisher confirmation with successful routing, and recorded publication flag. Publisher confirmation is broker acceptance, not consumer acknowledgment; never wait for another service or search visibility. The publication flag supports successful duplicate replay.
- On publication failure/unknown acceptance, attempt bounded deletion of the exact job, then throw. If deletion fails or is uncertain, log Critical/Fatal with both failures and throw cleanup-failure exception. Safe centralized HTTP errors reveal no internals.
- After known broker acceptance, a failed publication-status write retains the job and emits Critical/publication_unresolved rather than deleting or republishing accepted work.
- Recovery is out of scope. Accept crashes bypassing cleanup/logging, lost-confirmation messages remaining queued after deletion, possible duplicate delivery after recreation and manual investigation for unresolved rows. Compensation cannot undo RabbitMQ acceptance.

## Implementation status and migration decisions

Stages 1-9 are now implemented and verified against the revised requirements. The original prompt revision did not change production code; subsequent stage runs replaced the old ledger/outbox model with job-row idempotency and direct publication/compensation. Preserve existing uncommitted work. Determine applied migration history and preserve data; never silently rewrite applied history or delete a user database.

Historical work notes describe the architecture tested at that time and remain intact. Current shared requirements supersede their old scope. Revised Stage 6 keeps its original filename for reference compatibility, but implements direct publish and compensation, not an outbox.

## References

- [.NET hosted services and shutdown](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-10.0)
- [EF transactions](https://learn.microsoft.com/en-us/ef/core/saving/transactions)
- [Npgsql EF10](https://www.npgsql.org/efcore/release-notes/10.0.html)
- [RabbitMQ publisher confirmation](https://www.rabbitmq.com/tutorials/tutorial-seven-dotnet)
- [RabbitMQ reliability and duplicates](https://www.rabbitmq.com/docs/reliability)

Verify current APIs and compatible stable versions at implementation time. These links are design references, not evidence of completed code.
