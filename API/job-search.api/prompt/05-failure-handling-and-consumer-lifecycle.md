# Poison handling and bounded recovery

Read shared-requirements.md and requirements-review.md in this prompt directory first. Apply every shared constraint. Execute only this stage after previous stages have passed; preserve unrelated work.

## Tasks and acceptance

Implement transient failure classification and bounded cancellable consumer reconnect/pause with exponential backoff+jitter, no tight immediate requeue loop. Permanent malformed/unsupported/conflicting messages go through a search-owned durable quarantine exchange/queue: preserve original body only there (not logs), safe failure classification and original identities, persistent message, mandatory routing and publisher confirmation on a separate safe channel. ACK original only after quarantine acceptance. Quarantine unavailable/unknown acceptance leaves original unacked and pauses; document quarantine duplicates. Do not modify producer/source queue declarations. Poison record inspection/replay is manual and not implemented.

Implement drain: stop new deliveries, bounded in-flight wait, ACK only committed work, unacked on incomplete work, dispose resources; 30s host/45s container window. Separate broker/consumer ingestion health from DB read readiness, liveness no dependencies; low-cardinality metrics/safe centralized logs. No retry pipeline layered atop session reconnect or mandatory breaker without evidence.

Acceptance: controlled DB/broker downtime and recovery, poison/conflict followed by valid delivery, quarantine failure/lost confirm and no premature ACK, cancellation/backoff/drain/forced interruption; safe logs no data/secrets. Real broker tests separately identified from injected failures; coverage100%.

Run appropriate build/tests and mandatory exact 100% coverage gates. Write actual commands/results and remaining limitations in API/job-search.api/ai-log/job-search-api-work-notes.md. Do not proceed to the next stage automatically.
