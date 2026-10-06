# Job-row idempotency (revised Stage 4)

The coordinator accepts a strictly parsed request and all Idempotency-Key header values. Exactly one valid caller key is required on the first request. It hashes the exact UTF-8 key using SHA-256, normalizes the seven fields and computes the versioned canonical fingerprint. Neither current time nor generated IDs participates in the fingerprint. Different keys can intentionally create identical job content.

It reads the saved job by digest before future-date validation. A different fingerprint conflicts; unknown canonicalization versions fail safely. Matching published jobs return their original job/event IDs and response snapshot even after closing date expiry. Matching unpublished jobs return an unresolved outcome, never permission to publish again. There is no polling, automatic publication recovery or claim about an active owner's status.

| Application outcome | Meaning for the future endpoint |
| --- | --- |
| Created | This invocation inserted and confirmed commit of a new job; only this outcome may initiate direct publication |
| Published | Known completed duplicate; replay stored response without insert/publish |
| PublicationUnresolved | Existing or reconciled unpublished job; safe 503 publication_unresolved, no automatic repair |
| InProgress | Bounded database insertion contention; 409 idempotency_in_progress with Retry-After: 1 |
| Conflict | Same key, different payload; 409 idempotency_key_conflict |
| DependencyUnavailable | Database/commit reconciliation unresolved, including unknown fingerprint version |
| InvalidKey / InvalidRequest | Header or field failure without consuming a key |

The POST endpoint maps these outcomes to HTTP responses. Created alone is never 202: the workflow must confirm routable RabbitMQ acceptance and save publication status first. See [POST workflow](post-workflow.md).

New valid work inserts one job, with PostgreSQL's unique digest as authority. Concurrent losers dispose their failed transaction and reread through a clean context. There is no reservation service, process-local lock, ledger/outbox or lease. SQL/lock settings bound insertion contention. All matching contenders converge on the winning job/event, but only one is Created.

Uncertain commits receive at most one fresh diagnostic read. Finding an unpublished row returns PublicationUnresolved, not Created; missing/failed reads remain DependencyUnavailable. Never repeat creation in the same invocation or treat absence as rollback proof. Caller cancellation propagates. Recognized EF/Npgsql wrapper errors map safely; unexpected application exceptions reach central handling.

Keys remain for the lifetime of remaining jobs. Invalid new requests consume no key. Explicit compensation deletion removes the key with its exact job; a later attempt can recreate the posting and may duplicate an earlier uncertain broker delivery. No TTL or recovery scanner exists. Pending rows after crashes require manual investigation.

## Verification

Run from API/job-posting-api:

```sh
dotnet run --project tools/CoverageGate
dotnet test tests/JobPosting.Api.IntegrationTests --configuration Release --no-restore
dotnet build JobBoard.slnx --configuration Release --no-restore
```

Revised Stage 4 passed 164 isolated unit tests (763/763 lines, 194/194 branches, 119/119 methods), 16 host tests (42/42 lines, 4/4 branches, 1/1 methods) and 22 separate real PostgreSQL cases. Coverage exclusions remain unchanged. PostgreSQL tests force eight independent service providers/DbContexts to race, establish exactly one creator, verify equivalent/conflicting payloads, unresolved restart, published expired replay, lost commit acknowledgment, lock bounds, distinct-key identical content and recreation after compensation deletion. Publication timestamps in these tests are explicit state setup, not evidence that a RabbitMQ producer exists.

The database schema and migrations are in `src/JobPosting.Api/Persistence`.
