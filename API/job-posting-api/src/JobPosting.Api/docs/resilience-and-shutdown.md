# Resilience, readiness and shutdown (Stage 7)

The production `IJobEventPublisher` is a singleton `PublicationCircuit` around the reused RabbitMQ adapter. Polly.Core 8.8.0 supplies the non-HTTP circuit breaker; no retry strategy is registered. Every new job still has at most one direct publication attempt. An open breaker rejects without contacting RabbitMQ; the workflow then follows the same bounded compensation and safe 503 contract. Database uniqueness remains the idempotency authority across replicas; breaker state is local to each process.

## Bounds and configuration

| Setting | Initial value | Valid range |
| --- | --- | --- |
| RabbitMq:PublishBudgetSeconds | 8 | 1-10 seconds |
| RabbitMq:ConfirmTimeoutSeconds | 3 | 1-3 seconds, within publish budget |
| Resilience:FailureRatio | 0.5 | greater than zero, at most 1 |
| Resilience:SamplingSeconds | 60 | 1-60 seconds |
| Resilience:MinimumThroughput | 3 | 2-1000 |
| Resilience:BreakSeconds | 15 | 1-60 seconds |
| Host shutdown timeout | 30 seconds | fixed in this stage |
| Compensation timeout | 3 seconds | capped by remaining shutdown time |

Settings bind from normal ASP.NET configuration and are validated at startup. For example, `Resilience__MinimumThroughput=3` sets the minimum observations. The publish budget includes queueing for the channel, connection/topology setup, confirmation and best-effort resource reset. Confirmation is additionally limited to its own timeout. Task waits enforce local deadlines even if an adapter task does not promptly observe cancellation. Reset uses only remaining publish time (at most one second per resource), rather than adding another two seconds to an exhausted budget. These are asynchronous scheduling bounds, not real-time guarantees. SQL commands/locks retain their separate bounded settings; the publish budget is not an overall POST deadline.

A sample below minimum throughput stays closed even if all observed publications fail. At the threshold, a handled failure ratio of at least the configured ratio opens the circuit for the break duration. The next publication or readiness probe after expiry admits one half-open operation; concurrent calls are rejected. Success closes it; handled failure opens it again. Caller cancellation propagates and does not count as an outage failure. Adapter connection/confirmation timeouts count as publication failures. Readiness probe results share the same capability breaker/sample; health polling therefore contributes observations. There is no scheduled probe, transaction retry, message replay or cleanup retry.

## Health

- GET `/health/live` performs no external IO and returns 200 while accepting requests.
- GET `/health/ready` checks PostgreSQL connection capability and then RabbitMQ topology capability with one overall three-second token/wait bound; it returns 200 or a generic 503. It exposes no server names or failure text. It does not certify migrations, business data or downstream search processing.
- A RabbitMQ probe may establish the reusable connection and declare the configured durable topology, then passively verify exchange/queue access. It never publishes a job or dummy message. It can restore connection/topology capability after an outage, but never repairs unpublished rows.
- New requests, including health requests, receive 503 once draining starts. A readiness check cannot close an open breaker before its configured break duration expires.

## Structured diagnostics and metrics

JSON console logs retain UTC timestamps, trace scopes and request method/route-template/status/duration. Critical event 2001 (`PostingCleanupFailed`) records job/event IDs and separate publication/cleanup failure types; event 2002 (`PostingPublicationStateFailed`) records those IDs and the status-write failure type. Unexpected errors and disposal warnings also record safe failure types. Raw exception objects/messages/stacks are not serialized, because arbitrary driver or application exceptions may contain credentials, raw keys or payloads. Framework exception middleware and database/driver log categories that could duplicate unsafe exception text are filtered; the application supplies safe events. Safe 503/500 Problem Details carry the trace identifier without internal diagnostics.

The `JobPosting.Api` System.Diagnostics.Metrics meter (version 1.0.0) exposes:

| Instrument | Kind | Labels |
| --- | --- | --- |
| jobposting.requests | counter | status_class |
| jobposting.request.duration | histogram, ms | status_class |
| jobposting.publications | counter | outcome: confirmed/failed/cancelled/circuit_open |
| jobposting.compensations | counter | outcome: deleted/failed |
| jobposting.breaker.transitions | counter | state: open/half_open/closed |

There are no job/event/trace/key/payload labels or backlog/dispatcher instruments. A future metrics collector can subscribe to this standard meter; no exporter or public metrics endpoint is added in this stage.

## Graceful and forced stop

.NET's ConsoleLifetime handles SIGTERM and raises ApplicationStopping. `ShutdownDrain` stops admission, tracks each accepted request through its final publication/compensation, and computes cleanup time using an injectable monotonic clock. The host/Kestrel gets 30 seconds to drain. Cleanup cancellation is independent of request cancellation and gets at most three seconds or the remaining host window. Once requests drain, DI asynchronously disposes the reusable publisher/channel/connection; each resource reset wait is bounded. The implemented Stage 8 Compose uses a 45-second Docker stop grace; see [Docker/local development](docker-and-local-development.md).

Graceful stop can finish an in-flight publication and its saved 202, or finish compensating deletion and its safe 503. Forced kill, host deadline exhaustion or a process crash can leave unresolved rows and can bypass Critical logging entirely. Lost broker acknowledgement can leave a queued message even after database deletion. Readiness and restarting the process do not repair business state. Manual investigation remains required; see [POST workflow](post-workflow.md).

## Verification

From `API/job-posting-api`:

```sh
dotnet restore JobBoard.slnx --locked-mode
dotnet run --project tools/CoverageGate
dotnet build JobBoard.slnx --configuration Release --no-restore
dotnet test tests/JobPosting.Api.IntegrationTests --configuration Release --no-build --no-restore
dotnet list JobBoard.slnx package --vulnerable --include-transitive
```

Isolated unit tests use FakeTimeProvider for circuit sampling/breaks, exercise cancellation and one half-open probe, enforce publication timing including an unresponsive task, measure health timeout, and test cleanup at an expired shutdown deadline. Host tests verify the actual API health routes, startup validation, production safe errors and 30-second host configuration. They are measured separately from isolated units.

`tests/JobPosting.ShutdownHarness` is test-only, uses production controller/workflow/publisher/drain components, and adds controlled delays to expose signal windows. A separate Linux/Kestrel host uses a pinned .NET runtime and real disposable PostgreSQL/RabbitMQ containers. Integration tests send actual SIGTERM during publication/cleanup and SIGKILL during both windows, verify HTTP/database/broker outcomes, bound graceful exit, and verify forced exit 137 with unpublished rows and no Critical log. The harness is not the production Docker image or a replacement for Stage 8 verification; no test hooks enter the deployed API. Each fixture removes only its own containers/network/test image; ignored TestResults retains test publish artifacts.

Implementation references: [Polly circuit breaker](https://www.pollydocs.org/strategies/circuit-breaker.html), [Polly.Core 8.8.0](https://www.nuget.org/packages/Polly.Core/8.8.0), [Microsoft fake time provider](https://www.nuget.org/packages/Microsoft.Extensions.TimeProvider.Testing/10.1.0), [.NET Generic Host signal handling](https://learn.microsoft.com/en-us/dotnet/core/extensions/generic-host), [RabbitMQ .NET confirms and cancellation](https://www.rabbitmq.com/client-libraries/dotnet-api-guide).
