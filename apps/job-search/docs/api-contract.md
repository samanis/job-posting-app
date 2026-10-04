# Proposed search API contract

No backend is implemented. The exercise requires available listings, full details and eventual visibility of posted jobs. Everything below is a proposed integration contract needing agreement with the future search API. This app does not import posting-app code or synchronize browser storage with it.

## Requests

GET /api/jobs uses HttpParams: q (trimmed, maximum 200 characters), department/location (trimmed, maximum 100 each), optional opaque cursor (maximum 2048, no whitespace/control characters), limit (integer 1-50, default 20), sort (newest or closing-soon, default newest). Empty filters are omitted. Unknown runtime values for typed options are rejected without dispatch. Text case is preserved.

Proposed matching: q is a case-insensitive substring match on title/description; department and location are case-insensitive substring matches; supplied filters combine with AND. Matching/collation and indexing are server responsibilities. Empty query lists available jobs. Proposed availability: closingDate later than the current UTC calendar date; this differs deliberately from the posting form's user-local date validation and must be agreed before integration. The client does not exclude jobs using its own clock.

Proposed newest order: createdAt descending then id descending. Closing-soon: closingDate ascending, createdAt descending, id descending. Opaque server-issued cursors must bind filters/sort/limit and a stable read snapshot or equivalent keyset policy; server expiry/invalidation must return 409 with code cursor_expired. Cursor signatures, snapshot lifetime, collation, closed-job removal and stable ordering under concurrent writes remain unimplemented obligations. No page number or total count is inferred.

GET /api/jobs/:id uses encodeURIComponent on one bounded identifier (maximum 1000); blank, controls, lone surrogates and dot path segments are rejected. The response must refer to the requested id; mismatches are protocol errors. Closed detail records may be returned and are not rejected based on date; absent/removed records return 404/410. No POST or idempotency key is needed for these read-only requests.

## Schemas and validation

200 list: { items: JobSummary[], nextCursor: string | null }. At most the requested limit; unique ids; a non-null cursor requires a non-empty page. Summary fields: id, title, department, location (nonblank strings, maximum 1000 each), salaryMin/salaryMax (finite nonnegative decimals, at most two decimal places, min strictly less than max, bounded by MAX_SAFE_INTEGER/100), closingDate (real YYYY-MM-DD calendar date, year 0001-9999), createdAt (valid ISO timestamp with seconds, optional 1-3 millisecond digits, explicit Z/offset). No full description is requested on listings. Extra fields are discarded when immutable snapshots are made.

200 detail: same fields plus description (nonblank, maximum 100000). These response bounds are defensive client assumptions and must align with posting/API constraints; they are not claimed to be specified by the exercise. Data snapshots, item arrays and records are frozen. Descriptions remain plain text. Currency is unspecified. Empty items/null cursor is a successful empty result, not an error.

## Outcomes and retries

- Only a validated 200 establishes ready data. 202 is not-ready; 204, other 2xx and invalid/non-JSON success are protocol-error.
- 400/422 are invalid-query with safe client-authored messages. Raw validation/Problem Details payloads are not displayed. No server field-message UI is implemented at this stage.
- Detail 404/410 is unavailable; list 404/410 is a generic client error. List 409 with exact code cursor_expired is cursor-expired; other conflicts use generic client-error.
- 429 is throttled with optional retryAt epoch deadline. Retry-After accepts nonnegative integer seconds or strict IMF-fixdate. Invalid/nonfinite values give null; elapsed dates give now; valid delays are capped at 24 hours. This cap is a proposed manual-retry policy, not permission to automatically retry earlier than the server's limit; future UI should explain long throttling and allow revisiting later.
- Remaining 4xx are client-error; 5xx server-error; status 0 network-error; configured RxJS timeout timeout; unexpected failures unexpected-error. No special 401/403 behavior.

Each direct transport subscription sends a fresh GET; transport performs no caching, deduplication, polling or automatic retries. Unsubscribing aborts the request without an outcome. Timeout defaults to 10 seconds, clamps finite configuration to 1 ms-60 seconds, and uses 10 seconds for nonfinite configuration. Injectable QUERY_CLOCK supports deterministic delay parsing. Stage 4 QueryReads and the list workflow coordinate subscriptions and cache validated successes; see query-performance.md.

Server obligations remain unimplemented: independent read API, indexed filtering/keyset queries, safe cursor handling, bounded responses, read-model propagation from posted jobs, consistency policy, overload management and real throughput/latency verification. Browser fixtures cannot prove those properties.

Official implementation references: [Angular HTTP requests](https://angular.dev/guide/http/making-requests) and [HTTP testing](https://angular.dev/guide/http/testing).
