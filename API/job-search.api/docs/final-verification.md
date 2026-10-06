# Stage 9 verification — 2026-10-05

## Executed checks

Locked restore and Release solution build passed (six projects, zero warnings/errors). CoverageGate passed 144 unit tests and 25 host tests. Unit coverage: 794/794 lines, 448/448 branches, 172/172 methods, all 30 authored API files and 53 declared types. Host coverage: Program 37/37 lines, 4/4 branches, 1/1 method. Generated-only hash-checked EF exclusions are unchanged. Negative gates rejected missing/empty reports, uncovered lines/branches/methods and omitted source/type/module. This is exact unit/host coverage, not a substitute for external-system tests.

Release external integration tests passed 28/28 against disposable PostgreSQL and RabbitMQ. The frozen local producer-compatible event is delivered through the broker, committed once on redelivery and ACKed; its expired job is omitted from available lists but retained in detail. Tests cover available reads, concurrent cursor paging, conflicting identities, malformed events/quarantine, dependency outage/recovery and lifecycle boundaries.

`tools/Verify-Compose.ps1` passed: independently built API/migration images, separate disposable PostgreSQL, queued fixture before startup, successful migration gate, list/detail identity, non-root runtime, broker restart/reconnection, normal API/database stop/resume with retained data, and owned-resource cleanup. The real developer queue was not consumed or populated.

## Failure-injection limits

Database pause/resume and broker stop/restart exercise real dependency failures. SQL commit uncertainty is injected after a real SQL commit; this is not a physical lost database network response. Quarantine confirmation uncertainty is injected after real broker confirmation and demonstrates possible duplicate quarantine publication. ACK/redelivery and channel ownership are tested. Forced drain uses a callback/deadline boundary, not OS SIGKILL. Compose normal stop exercises Docker shutdown, but no claim is made of SIGTERM under concurrent in-flight production load. Wire compatibility uses a frozen independent fixture, not a running posting application.

## Performance evidence

The retained [Stage 7 report](performance/latest.json) records 10,000 seeded rows, four concurrent clients, 10.009 seconds, 4,146 requests, 414.2 requests/sec, p50 8.19 ms, p95 21.32 ms, p99 25.64 ms and zero errors. PostgreSQL plans demonstrate ordering and trigram indexes. This local warmed loopback/synthetic workload disables logging and consumption; it is not production capacity evidence and was not rerun in Stage 9. See [methodology](performance/README.md).

## Independence and remaining scope

API source has no posting ProjectReference, source import, posting migration, posting HTTP call or posting database access. Tool references point only to local search source. Broker exchange/routing strings are wire contracts. Required build/runtime examples, locks, manifests and Compose files are eligible for Git; real env secrets and generated output are ignored.

This completes the selected independent search API prompts, not the entire PDF submission. Root full-stack Compose, connected Angular flow/proxy or CORS validation, authentication/authorization, production load/security evaluation, metrics export, updates/deletes and automated replay remain outside this implementation. No genuine conversation transcript export or public submission was created; work notes are not a transcript. No commit, push or deployment was performed. Frontend differences are listed in the [handoff](client-integration-handoff.md).
