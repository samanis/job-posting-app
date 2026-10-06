# Verification and operational handoff

Stage 9 verifies the posting API subset, not the complete PDF exercise or end-to-end search. See the [client handoff](client-integration-handoff.md), [contract](../src/JobPosting.Api/docs/api-contract.md), [workflow](../src/JobPosting.Api/docs/post-workflow.md) and [Docker runbook](../src/JobPosting.Api/docs/docker-and-local-development.md).

## Reproduce verification

From API/job-posting-api, with .NET 10 and Linux Docker available:

```sh
dotnet run --project tools/CoverageGate
dotnet build JobBoard.slnx --configuration Release --no-restore
dotnet test tests/JobPosting.Api.IntegrationTests --configuration Release --no-build --no-restore
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Verify-Compose.ps1
```

> Later change: `tools/Verify-Compose.ps1` and this API's standalone Compose file were removed in favour of the repository's root `docker-compose.yml`. The record above describes the checks as run at the time.

The gate runs locked restore. Integration and Compose checks create isolated containers/volumes and delete only their owned resources. Do not replace these checks with deletion of a developer's normal Compose volumes.

| Evidence | Scope |
| --- | --- |
| Isolated unit tests | Strict JSON/header/body/decimal/date rules, canonical fingerprints, replay outcomes, rollback/uncertain commit injection, broker nack/return/timeouts/cancellation, compensation failure/timeout, safe Critical logs, circuit transitions and cleanup budgets. |
| In-process host tests | Real HTTP routing, full-record 202, 400/422/409/503/500, 413/415, safe exception mapping, startup/DI and health behavior. Dependencies are test substitutes. |
| Real PostgreSQL/RabbitMQ tests | Migrations/constraints, independent-provider equal/conflicting races, expired replay, transaction rollback, simulated lost commit acknowledgment after actual commit, persistent broker messages/restart, mandatory unroutable publication, direct workflow and compensation. |
| Linux signal tests | Real SIGTERM drains publish/cleanup; SIGKILL leaves unresolved records and cannot guarantee cleanup or logs. Uses a dedicated test harness, not a production delay setting. |
| Production Compose check | Clean isolated databases, explicit migration service, non-root API/no SDK, internal networking, 202/replay/conflict, durable restart/recreation, broker outage compensation/recovery and bounded stop. |

Lost broker acknowledgment is deliberately injected after a REAL broker confirmation. Nack/timeout branches are controlled unit adapter tests; this does not claim that a physical network dropped an acknowledgment or that a real broker was forced to nack. Replaying a completed response models HTTP response loss without republishing. Integration evidence is separate from coverage.

Coverage includes all authored production source/type sets. Unit reports exclude Program, which has a separately measured host bootstrap gate. Generated-only exclusions are the three hash-checked EF designer/snapshot files in coverage-exclusions.json and generated build output; authored migration Up/Down remains included. Negative checks must reject missing/empty reports, uncovered lines/branches/methods and omitted source/type/module. Reports are under tests/JobPosting.Api.Tests/TestResults/unit and host, and are intentionally ignored.

## Manual investigation

The job row holds the unique key digest, fingerprint/version, stable response, event ID and nullable PublishedAt. There is no ledger, outbox, lease, dispatcher or automatic retry/repair. New creators commit first, publish once, then record confirmation. Publication exceptions trigger one exact-job/event compensating deletion with an independent three-second token, bounded further by remaining shutdown time.

For unresolved failures, preserve the trace/job/event identifiers and inspect authorized database and broker evidence. Critical event 2001 means cleanup failed or its outcome is uncertain; event 2002 means publication was confirmed but recording PublishedAt failed. Logs intentionally contain safe failure types and identifiers, not submitted payloads, keys, credentials or exception text/stacks.

A null PublishedAt does NOT prove RabbitMQ never accepted the event. An absent row does NOT prove no message was queued. Do not automatically delete, republish or mark a record confirmed merely from those observations. Investigate the actual job/event and delivery evidence before choosing a manual action. No repair tooling or public administrative endpoint is supplied.

Compensation cannot retract a queued event. Once deletion releases a key, recreation can duplicate downstream jobs. Crashes/forced stops can bypass cleanup and logging or leave committed jobs without publication. These duplicate/lost-delivery gaps are accepted in this reduced scope. A future consumer must deduplicate event IDs; different recreated event IDs still require an explicit downstream policy.

Readiness verifies bounded dependency access/topology, not search visibility or a completed projection. Liveness does not call dependencies. The API is unauthenticated and no exporter/monitoring backend is configured. Operations details and safe volume handling are in the linked runbooks.

## Remaining work

Angular transport/202/key/timezone integration, search API and consumer, separate query database, authentication/authorization and a genuine AI transcript export remain deferred. Work notes describe actions and results; they are not a fabricated conversation transcript. Docker verification covers Linux amd64; an arm64 target mapping exists but has not been verified. Pinned image/package versions are a tested baseline, not a claim of latest servicing.

Required inputs must be committed before another developer can pull this implementation. Verification of an uncommitted working tree is not proof that the remote checkout already contains those files. This stage does not commit, push or deploy.

## Final run: 2026-10-05

- Locked restore/coverage gate passed: 223 isolated unit tests, 1093/1093 lines, 344/344 branches, 193/193 methods; 39 authored files and 49 declared types.
- Separate host gate passed: 28 tests; Program 55/55 lines, 4/4 branches, 1/1 method. Both gates passed all intentional negative checks and source/type completeness checks.
- Release build passed for all five projects with zero warnings/errors.
- External integration suite passed: 36 tests, zero failed/skipped, including real PostgreSQL/RabbitMQ and Linux signal scenarios.
- Disposable production Compose verification passed all six scenario groups; its owned containers/volumes/images were removed. Existing development data was preserved.
- All 118 application inputs reported by Git (tracked plus untracked) were not ignored, including .env.example; local secrets and build/test reports were ignored. Local handoff/README/contract links resolved; git diff --check passed.

No production code or Angular changes were needed in Stage 9. There are no verification blockers. Backend implementation files from earlier stages and these handoff updates remain uncommitted; the remote repository is not yet a complete fresh-checkout delivery of this working tree.
