# RabbitMQ consumer and SQL projection (Stages 4?5)

Search uses the shared broker but depends on no posting project, service endpoint or database. Every valid delivery writes only to the separately configured job_search PostgreSQL database. Source queue job-post-queue has competing consumers: multiple search replicas share workload rather than each receiving a separate copy. Integration fixtures never read the developer queue.

## Configuration and startup

RabbitMq section/environment keys: Enabled(defaulttrue), HostName(localhost), Port(5672), UserName/Password(guest local-only defaults), VirtualHost(/), Exchange(job-post-exchange), Queue(job-post-queue), RoutingKey(job-posting.created.v1), Prefetch(10), Concurrency(1). Prefetch allowed1..100; concurrency1..10 and<=prefetch. Names bounded255 UTF8 bytes/nonreserved; required fields validated on startup without echoing secrets. Use configured broker credentials, not guest across Docker networks. RabbitMQ.Client7.2.2 is pinned; automatic/topology recovery disabled so replacement sessions cannot inherit delivery tags.

DI registration creates no broker connection. The hosted worker starts asynchronously and then connects. Migrate/provision the separate search database first (see persistence.md); it never starts or migrates posting. SearchConsumerWorker opens replacement sessions after failure using cancellable exponential backoff with jitter: nominal 1 second initially, doubling to a nominal 30-second cap, with each wait between 80% and 100% of that value. A session lasting at least 30 seconds resets failure backoff. No business projection write retry happens inside the same delivery attempt.

For host mode from API/job-search.api, configure search credentials/connection externally and match the existing local broker:

```powershell
$env:RabbitMq__HostName='127.0.0.1'
$env:RabbitMq__UserName='jobposting'
# Set RabbitMq__Password from your configured local broker secret, without committing it.
# Set SearchDatabase__ConnectionString and PGPASSWORD for the SEPARATE search database.
dotnet run --project src/JobSearch.Api --launch-profile http
```

RabbitMq__Enabled=false deliberately disables ingestion (used by host HTTP tests); it does not connect or fabricate projection success. No API/search query routes exist yet. No developer broker/database was exercised by this stage, only isolated test resources.

## Delivery/acknowledgment boundary

IConsumerSession is the broker lifecycle boundary; ISearchEventHandler reads the local versioned event using EventReader and calls ISearchProjection. SearchEventHandler authorizes Inserted or Duplicate only after ProjectionStore returns committed/verified state; invalid or identity-conflicting data raises a safe permanent rejection. Unknown commit/SQL/cancellation failures propagate without authorization. Metadata mapping accepts byte/sbyte/short/ushort/int or int-range long schemaVersion headers; wrong AMQP types are rejected before projection. Optional metadata is validated against the body (see contracts.md).

The RabbitMQ adapter creates a long-lived session connection/channel, declares compatible durable direct exchange, durable nonexclusive/non-auto-delete queue and binding WITHOUT source DLX arguments or purges, then applies per-consumer QoS and consumes with autoAck=false. Callback scope keeps borrowed body memory alive through handling; no borrowed memory is retained after return. Each callback creates an application DI scope. Processing semaphore and client dispatch concurrency bound active work; prefetch bounds outstanding deliveries, not total bytes beyond body validation.

After successful handling, a separate semaphore serializes ACK calls. ACK uses multiple=false on the original channel captured for that callback, checks cancellation and that channel is still open, and is attempted once. No replacement channel receives a stale delivery tag. ACK errors end the session; there is no fallback ACK/NACK or same-tag retry. A SQL commit before lost ACK/connection produces redelivery, which verifies the retained event/job/hash and becomes a no-op. Different recreated job/event IDs remain distinct.

Callback failure, channel shutdown or consumer unregistration signals the session to close. On failure, its processing cancellation ends incomplete work; unacknowledged deliveries are released by channel/connection disposal for broker redelivery. Setup and RPC/socket operations have3s client timeouts; resource disposal is best-effort bounded3s per channel/connection and logs only failure type. One cancellation-aware SQL operation remains separately bounded by persistence settings. A dependency ignoring cancellation may finish after the session closes; its old callback cannot ACK on a new session. Read work is not coupled to posting or consumer acknowledgment back to producer.

## Quarantine and recovery

Permanent malformed, unsupported-version, metadata and identity-conflict deliveries are published to a separate search-owned durable direct exchange and durable queue. Defaults: QuarantineExchange=job-search-quarantine-exchange, QuarantineQueue=job-search-quarantine-queue, QuarantineRoutingKey=job-search.rejected.v1. Names must differ from the source topology. The source queue is never purged or redeclared with DLX arguments. Original bytes are preserved only in quarantine; persistent properties include original message/correlation IDs, safe failureCode and sourceContentType. Logs contain classification and exception type, never event bodies, connection strings or exception messages/stacks.

IQuarantinePublisher isolates the transport. RabbitMQ publication uses its own connection/channel with mandatory routing and publisher confirmation tracking enabled. Each rejected delivery gets one publication attempt within QuarantineBudgetSeconds (default8, allowed1..10). The source delivery is acknowledged only after publication returns with broker confirmation. A return/NACK/error/timeout leaves it unacknowledged; the session closes and the worker backs off. Missing confirmation does not prove missing acceptance: redelivery may produce duplicate quarantine records. Quarantine acceptance says nothing about downstream search clients or other services. Original delivery tags are never reused on another channel.

Inspection and repair/replay are manual. A broker operator can inspect job-search-quarantine-queue and its reason headers, correct the original problem, and deliberately republish a valid versioned event through the source routing contract. No automatic replay, management endpoint or operational repair tooling is implemented. Do not delete an entire queue to repair one delivery. Quarantine needs persistent broker storage in the later Docker stage.

## Shutdown, health and metrics

A graceful stop closes application admission, cancels the broker subscription with noWait=true, then waits for admitted callbacks within DrainSeconds (default24, allowed1..24). Already admitted work may commit and ACK on its original channel. New or incomplete deliveries stay unacknowledged. Deadline expiry cancels processing; channel and connection cleanup each have a 3-second bound, reserving a total 30-second host shutdown budget. The later container stage must provide at least45 seconds. Managed semaphore objects remain valid for late callbacks until garbage collection; a late callback checks the canceled token and cannot acknowledge a stale delivery.

GET /health/live has no external dependency. GET /health/ready checks the separate search database's exact applied migration set and readable jobs table within3 seconds; it never migrates anything. Read readiness does not require RabbitMQ. Draining/stopped instances report503. GET /health/ingestion reports200 only for Running; Disabled/Connecting/Backoff/Draining/Stopped report503 with a bounded state name. Ingestion disabled is a deliberate configuration choice, not successful ingestion. Stage6 supplies GET /api/jobs and GET /api/jobs/{id}; their reads stay usable during broker outages when the search database is available.

System.Diagnostics.Metrics meter JobSearch.Api publishes search.projections(outcome), search.redeliveries, search.quarantines(outcome), search.consumer.transitions(state), search.request.duration(status_class, milliseconds). Labels are bounded status/state/outcome values; no job ID, message ID or payload is a metric label. An exporter is not configured in this stage. There is no nested delivery retry pipeline or circuit breaker layered over reconnect.

## Verification

```sh
dotnet run --project tools/CoverageGate
dotnet build JobSearch.slnx --configuration Release --no-restore
dotnet test tests/JobSearch.Api.IntegrationTests --configuration Release --no-build --no-restore
```

Isolated unit tests verify authorization ordering, invalid/conflict/unknown/canceled work, startup bounds, schema-header mapping, setup/resource failure, channel shutdown/unregistration, ACK failure, backpressure and late old-session completion. Host tests disable external ingestion while checking configuration and safe HTTP bootstrap behavior. External tests use isolated GUID-named PostgreSQL and RabbitMQ containers/queues: published local producer-compatible fixture becomes one row; duplicate deliveries are acknowledged; controlled interruption AFTER real SQL commit BEFORE ACK causes redelivery and durable duplicate; two independent providers compete; prefetch2/concurrency1 leaves exactly3 of5 messages ready while handling is blocked. Post-close queue inspection verifies no pending delivery remains after completed cases. Queue-ready count alone does not prove ACK, so tests also close the sessions and inspect for released unacked messages.

The interruption is deliberate test instrumentation, not a claim that a physical network lost an acknowledgment. The integration broker alone receives fixture publications; real development queue is preserved. These tests contribute no unit coverage. Exact100% unit/host gates and hash-checked generated-only EF exclusions remain in effect.

Reference: [official RabbitMQ .NET client guide](https://www.rabbitmq.com/client-libraries/dotnet-api-guide), especially manual acknowledgments, consumer memory lifetime and channel concurrency. No posting class is imported to establish interoperability.

Stage5 tests add real poison/conflict quarantine followed by valid work, real quarantine acceptance followed by an injected lost-confirmation error (two quarantine copies, original redelivery), graceful completion and deadline cancellation, actual owned PostgreSQL container pause/resume and actual owned RabbitMQ restart with automatic worker reconnection. Forced drain is a cancellation deadline test, not an OS process-kill/SIGTERM test. Unit tests cover broker publish failure and no premature source ACK; real integration also covers incompatible search-owned quarantine topology without changing source declarations. Fixtures touch no developer database, queue or container.

See [publisher confirms and consumer acknowledgments](https://www.rabbitmq.com/docs/confirms) and [worker lifecycle](https://learn.microsoft.com/en-us/dotnet/core/extensions/workers). These are independent acceptance boundaries; this consumer does not require confirmation from a downstream application.
