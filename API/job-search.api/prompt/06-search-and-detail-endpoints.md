# Indexed read API and cursor paging

Read shared-requirements.md and requirements-review.md in this prompt directory first. Apply every shared constraint. Execute only this stage after previous stages have passed; preserve unrelated work.

## Tasks and acceptance

Implement GET /api/jobs and GET /api/jobs/{id} against search PG only. Use shared query contract, literal case-insensitive substring escaping/parameterization, AND filters and UTC future closingDate. Capture clock once; summary excludes description, detail includes closed records. Return authoritative source IDs/times, UTC millisecond timestamps, bounded200 list envelope/nextCursor or detail; safe400/404/409/503/500, no write endpoints. No DB unavailable fallback to posting/memory.

Implement versioned HMAC authenticated keyset cursor with constant-time MAC validation, bounded decoding, filter/sort/limit/UTCdate/upper committed ingestion watermark/last key/expiry binding, strict shape. Signing secret externally configured/shared replicas; development example clearly nonproduction, prod rejects defaults. 409 cursor_expired only for authentic expiry/retired version, tampering400. LIMIT+1, no offset/count, immutable snapshot excludes later commits; test commit-order watermark boundary. Future-deployed copies support consistent keys and documented rotation.

Acceptance: real PG seeded filters including literal %/_/backslash, both sorts/ties, empty list, closed detail, invalid UUID, midnight, multipage no gaps/duplicates under concurrent new ingestion; signed tamper/filter mismatch/expiry/oversize cases; host HTTP/schema/error tests. No Angular changes; coverage100%.

Run appropriate build/tests and mandatory exact 100% coverage gates. Write actual commands/results and remaining limitations in API/job-search.api/ai-log/job-search-api-work-notes.md. Do not proceed to the next stage automatically.
