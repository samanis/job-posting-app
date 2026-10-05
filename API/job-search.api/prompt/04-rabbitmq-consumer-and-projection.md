# RabbitMQ consumption with commit-before-ACK

Read shared-requirements.md and requirements-review.md in this prompt directory first. Apply every shared constraint. Execute only this stage after previous stages have passed; preserve unrelated work.

## Tasks and acceptance

Implement broker-neutral message handling/projection service and RabbitMQ adapter/background service with manual ACK, bounded prefetch/concurrency, reused connection/channel and cancellable lifecycle. Declare compatible existing exchange/queue/binding or verify topology; never alter/purge source queue or add incompatible arguments. Handle one source event transaction per delivery. ACK only committed insert or confirmed identical durable duplicate. Fresh read may resolve commit uncertainty; unknown state stays unacked. Protect channel-scoped tags and ACK operations, including channel replacement. Session reconnection must not reuse stale tags or double-settle messages. Search must not call posting endpoints or notify producer of processing success.

Acceptance: unit ACK ordering/duplicate/conflict/cancel/unknown commit/channel loss tests plus isolated real PG/Rabbit published wire fixture becomes one job. Simulate commit-before-ACK interruption and redelivery proving no second row. Competing consumers from independent service providers still one projection. Bounded backpressure demonstrated. Coverage100%.

Run appropriate build/tests and mandatory exact 100% coverage gates. Write actual commands/results and remaining limitations in API/job-search.api/ai-log/job-search-api-work-notes.md. Do not proceed to the next stage automatically.
