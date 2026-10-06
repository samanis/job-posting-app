# Direct RabbitMQ producer (Stage 5)

`IJobEventPublisher.PublishAsync` accepts a broker-neutral immutable `JobPostingCreated` envelope and cancellation token. `FromSaved` copies the complete authoritative saved record and stable event ID into it; no idempotency digest/fingerprint or publication flags enter the message. There is no database envelope/outbox, consumer, dispatcher or message retry. The POST endpoint calls the producer only for Created jobs and coordinates publication status/compensation; see [POST workflow](post-workflow.md).

The adapter uses pinned [RabbitMQ.Client 7.2.2](https://www.nuget.org/packages/RabbitMQ.Client/7.2.2), targeting a compatible .NET8+ baseline. `CreateChannelOptions(true,true)` enables publisher confirmations and tracking. Awaiting mandatory `BasicPublishAsync` rejects negative acknowledgment and returned/unroutable messages through PublishException. See the [official confirmation guide](https://www.rabbitmq.com/tutorials/tutorial-seven-dotnet) and [client API guide](https://www.rabbitmq.com/client-libraries/dotnet-api-guide). Broker acceptance is never consumer acknowledgment or completed search indexing.

## Topology and wire contract

Durable direct exchange `job-post-exchange`, routing key `job-posting.created.v1`, durable nonexclusive queue `job-post-queue` and binding are declared once per connected publishing channel. Messages are persistent JSON/UTF-8 with MessageId = stable event UUID, CorrelationId = diagnostic correlation, Type = JobPostingCreated, UTC AMQP timestamp and schemaVersion header 1. JSON fields are eventId, eventType, schemaVersion, occurredAt, correlationId and job (all seven saved fields, id and createdAt).

The singleton adapter reuses one managed connection/channel. A semaphore serializes access to that channel across concurrent calls; it is a local resource guard, not an idempotency authority or cross-host reservation. Wait time counts against the publish budget. Closed resources are discarded; the next invocation connects and declares topology afresh for its new message. Client automatic/topology recovery are disabled: connection repair never replays business messages.

A failed publication is attempted once. PublishException (nack/return) and failures before sending report NotAccepted. Other failures after publication starts report AcceptanceUnknown conservatively: socket/confirmation loss does not prove rejection. Caller cancellation propagates as OperationCanceledException. Internals remain on server exception chains; the public exception message is generic. Resource-reset failures log warnings and do not mask the publication failure.

## Configuration and bounds

Options section: RabbitMq, bound through normal ASP.NET configuration and validated on startup. Registration/startup creates no broker connection; first explicit publication does. Invalid option errors identify keys without submitted secrets.

| Setting | Default |
| --- | --- |
| HostName / Port | localhost / 5672 |
| UserName / Password / VirtualHost | guest / guest / / (local-only client defaults) |
| Exchange / Queue / RoutingKey | job-post-exchange / job-post-queue / job-posting.created.v1 |
| PublishBudgetSeconds | 8 (allowed 1-10) |
| ConfirmTimeoutSeconds | 3 (allowed 1-3, no greater than budget) |

Topology names must be nonempty, at most 255 UTF-8 bytes and not start with reserved amq. Connection, handshake, RPC and socket timeouts are 3 seconds. An overall linked token bounds gate wait/setup/publication; a nested confirmation token cannot extend that budget. Failed-resource disposal is best effort, at most one second per resource within the remaining publish budget; it does not extend that budget. SQL operations and compensation have separate bounds. See [resilience and shutdown](resilience-and-shutdown.md).

The current API adds circuit breaking, safe HTTP error mapping, compensation, readiness and a 30-second host drain. Async disposal waits for active serialized work and bounds resource reset. This local demo uses plain AMQP and local-only credentials; deployment security is not added here.

## Local broker

The integration fixture creates and removes its own broker container. For a persistent local broker, use the root Compose service from the repository root:

```sh
docker compose up -d rabbitmq
```

Then point a host-run API at it (PowerShell shown; use `export` on macOS/Linux):

```powershell
$env:RabbitMq__HostName = '127.0.0.1'
$env:RabbitMq__UserName = 'jobboard'
$env:RabbitMq__Password = 'local-development-only-change-me'
dotnet run --project src/JobPosting.Api --launch-profile http
```

The management UI is http://localhost:15672 with the same local credentials. The API also needs PostgreSQL; see [Docker and local development](docker-and-local-development.md#run-the-api-on-the-host) for the full set of variables. Use a root `.env` file or secret configuration for real passwords.

## Verification and limitations

```sh
dotnet run --project tools/CoverageGate
dotnet test tests/JobPosting.Api.IntegrationTests --configuration Release --no-restore
dotnet build JobBoard.slnx --configuration Release --no-restore
```

Stage 5 unit suite: 178 cases, 876/876 lines, 254/254 branches, 147/147 methods across 32 authored files/37 types. Host suite: 19 cases, 43/43 lines, 4/4 branches, 1/1 methods. Both reach 100% with source completeness/negative checks; exclusions remain unchanged.

Separate integration evidence: 22 PostgreSQL cases plus three RabbitMQ cases. Broker tests verify eight concurrent confirmed messages with complete envelope/properties, mandatory unroutable rejection followed by capability repair (no rejected-message replay), and persistent message survival across a real container restart followed by publication of a new message. No search service runs. Test-only BasicGet inspects queue contents, not a production consumer. Owned containers/volumes and generated results are cleaned up/ignored respectively.

Nack, confirmation timeout, lost acknowledgment, caller cancellation, disposal errors and safe interface concurrency are deterministic isolated tests over standard RabbitMQ interfaces; they are not claimed as injected real-broker nack/lost-ack scenarios. Integration results do not contribute to unit coverage. The client package audit reports no known vulnerable dependencies in the tested locked graph.

Persistence does not guarantee exactly-once messaging. Lost broker confirmation followed by later compensation may leave a queued message without its job row, and recreation can duplicate delivery. Broker restart verification proves retained accepted messages in this local durable setup, not automatic recovery of failed postings or production cluster HA.
