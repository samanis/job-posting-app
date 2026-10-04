# Stage 2: Query contract and HTTP outcomes

Read apps/job-search/prompts/shared-requirements.md. Stage 1 must be complete. Implement only this stage.

1. Write docs/api-contract.md distinguishing exercise requirements, chosen assumptions and unimplemented server obligations. Finalize parameter limits, matching/open-job semantics, deterministic sort, cursor pagination and list/detail schemas. Align job field names with posting without coupling independent app source trees.
2. Define immutable summary/detail/query/page types and discriminated outcomes. Runtime-validate all bodies: field types, finite valid salary bounds, date-only/calendar validity, timestamp validity, bounded item count, unique ids and valid bounded cursors. Do not reject closed job details solely because their closing date passed.
3. Implement injectable GET transport with HttpParams, safely encoded detail ids, full response inspection and bounded configurable timeout. Never concatenate unescaped user query text into URLs. Keep cache/UI state outside transport.
4. Classify 200 valid/empty results, unsupported 2xx (including 202 and 204), malformed bodies, 400/422 query errors, detail 404/410, documented cursor-expired 409, generic other 4xx, 429, 5xx, network and timeout. Cancellation must not become a user error. Do not leak raw HTML, stack traces or arbitrary server payloads.
5. Parse Retry-After seconds/dates with injected clock, explicit invalid/elapsed behavior and capped safe delay. No automatic retries or auth-specific handlers.

Acceptance: HttpTestingController asserts exact method/parameters, empty-query omission, encoding and detail path. Tests cover every outcome, malformed boundary and retry-delay branch. Build and relevant tests pass. All backend behaviors remain proposed.
