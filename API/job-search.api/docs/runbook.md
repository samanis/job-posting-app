# Operational runbook

Follow [Docker and host setup](docker.md) for external secrets, separate PostgreSQL, explicit migration execution, existing broker networking and ports. Normal Compose starts no broker and references no posting application. Preserve the normal search database volume when stopping; volume deletion destroys projection/idempotency history. Never use disposable test cleanup commands against developer resources.

Use `/health/live` for process liveness, `/health/ready` for migrated search database read readiness, and `/health/ingestion` for consumer state. Broker outages need not disable reads of already projected jobs. Database outages stop successful ingestion and produce safe read dependency errors. Check safe state/failure codes and trace IDs; do not enable raw driver or request logging to diagnose production incidents.

The consumer reconnects with bounded backoff. Manual ACK follows verified projection commit. On shutdown, admission closes and subscription cancellation precedes a bounded drain; default host timeout is 30 seconds, drain 24 seconds, Compose grace 45 seconds. Unfinished deliveries remain unacknowledged for redelivery. Do not expect exactly-once transport delivery.

For quarantine, use the [manual handling procedure](messaging.md): inspect authorized messages safely, diagnose the safe code, and make an explicit correction/replay decision. No automatic quarantine replay or destructive purge is provided. Failed/uncertain quarantine confirms can create duplicate quarantine messages. Replaying conflicting identities without correcting the underlying conflict simply quarantines again.

Configure production cursor keys externally and consistently across replicas. Rotate by distributing old/new keys, changing the active key, then retaining the old key through the maximum outstanding cursor lifetime. See [search configuration](search.md). Secrets and generated verification env files are ignored; `.env.example` is a tracked input with blank secret values.

Run `tools/Verify-Compose.ps1` only for its labelled disposable project/network/database/broker. It builds independent images and cleans its own resources. Use [final verification](final-verification.md) to interpret test evidence and limits; use [client handoff](client-integration-handoff.md) before connecting Angular.
