# Search read API (Stage 6)

Both routes use only the separately configured search PostgreSQL database. No posting code/database/HTTP dependency or in-memory fallback exists. Query records are immutable create-only projections. No public POST/PUT/DELETE route is implemented.

Successful first-page responses use the local output cache for at most 15 seconds,
capped at the next UTC midnight. The cache key includes the UTC day so a new day's
request cannot reuse yesterday's availability snapshot. New ingestion is visible
after this short freshness window. Requests already in flight across midnight may
finish with their captured day, but are marked no-store and are not stored.
Continuation responses are no-store and bypass output-cache lookup/storage: every
request validates the cursor, including its expiry. Immutable successful details
remain cached for one hour on the server and one day downstream. Errors are not cached.
The low-cardinality search.cache.hits counter distinguishes list and detail hits.

GET /api/jobs accepts q (title OR description substring, up to200 trimmed UTF16 characters), department/location (up to100), limit1..50(default20), sort=newest(default) or closing-soon, and optional cursor<=2048. Filters combine with AND. Blank filters are omitted; unknown/duplicate parameters, controls, invalid limit/sort and malformed cursors return400. Percent, underscore and backslash are literal text: parameterized ILIKE uses explicit backslash escaping. SQL predicates/order/projection run on the server with AsNoTracking and LIMIT+1; there is no OFFSET or total count. List rows do not retrieve descriptions.

Availability is closingDate strictly greater than captured TODAY UTC. This is a deliberate existing client alignment, not a PDF timezone requirement; posting's Toronto validation can differ. Details accept a nonzero D-format UUID, return400 for malformed,404 for missing, and200 for existing closed records. Source IDs and source created timestamps are authoritative. Response dates are yyyy-MM-dd; timestamps are UTC ISO with exactly3 fractional digits, truncating display precision only. Original source ticks remain unchanged in persistence and fingerprinting. Summaries omit description; details include it. Currency remains unspecified.

```text
GET /api/jobs?q=engineer&location=toronto&limit=20&sort=newest
200 {"items":[...],"nextCursor":null|"opaque-token"}
GET /api/jobs/{source-job-uuid}
200 {"id":...,"createdAt":...,"title":...,"department":...,"location":...,"description":...,"salaryMin":...,"salaryMax":...,"closingDate":...}
```

## Stable pages and cursors

Newest sorts database createdAt DESC, UUID DESC. Closing-soon sorts closingDate ASC, createdAt DESC, UUID DESC. Continuation uses PostgreSQL row comparison for the descending time/UUID tuple, never .NET string/UUID ordering or offset scanning. PostgreSQL timestamp precision is microseconds; the cursor boundary uses the actual stored timestamp, while source precision is used for outgoing DTOs. The UUID tie-breaker provides a complete order.

The first page captures TimeProvider.GetUtcNow once and reads the maximum committed ingestion sequence. Every page filters sequences <= that upper watermark. The existing write protocol holds a transaction-scoped advisory lock before sequence allocation through commit; a pending lower sequence cannot later become visible beneath a newer committed maximum. A plain PostgreSQL sequence alone would not provide that guarantee. Later ingestions are excluded even if their source date sorts ahead or behind existing jobs. Immutable records make the bounded snapshot stable; this is not a general MVCC snapshot or update/delete protocol.

A cursor is canonical base64url JSON plus a separate HMAC-SHA256 tag. Its strict, bounded shape contains version, signing key ID, SHA256 binding of normalized q/department/location/limit/sort, captured UTC day, upper committed watermark, last time/UUID/closing-day tuple, original issue time and expiry. The MAC authenticates all payload bytes with constant-time comparison. Filters are hashed to keep worst-case Unicode queries comfortably inside the2048-character bound. The cursor is authenticated, not encrypted. Extra/duplicate fields, noncanonical encoding, bad signatures, missing keys or query binding mismatch are400 invalid_cursor. Only an authentic otherwise-valid expired or unsupported/retired-version token produces409 cursor_expired. Expiry is not refreshed per page. Default TTL15 minutes, configurable1..60. UTC midnight does not change availability within a still-valid chain; a new chain captures a new day.

## Signing configuration and rotation

Configure the SAME key ring, active key ID and lifetime on every replica using external environment/secret configuration. Keys are base64 encoding of at least32 random bytes, with1..4 keys; IDs are1..32 ASCII alphanumeric/hyphen characters. Startup validates without echoing keys.

```powershell
# Generate a secret with a cryptographically secure source; store it outside the repository.
$env:Cursor__Keys__current = [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$env:Cursor__ActiveKeyId = 'current'
$env:Cursor__LifetimeMinutes = '15'
```

Generate once and securely distribute the same key to replicas; do not generate a different key on each startup. Development ONLY supplies a public, known nonproduction example key when no ring is configured. Production/Staging reject empty rings, short/malformed keys and that known development key. Do not use the development key for exposed environments.

To rotate, first distribute a ring containing old and new keys to all replicas, retaining the old active ID; then switch the shared active ID to new. Existing tokens remain valid under retained old keys until their original expiry. Keep old keys for the maximum outstanding TTL (up to60 minutes plus clock tolerance) before removal. Removing a key makes its old tokens invalid_cursor400 because their authenticity can no longer be established, rather than claiming authentic expiry409. Coordinate clocks/configuration and protocol retirement across deployments; do not independently change a key's material under the same ID.

## Errors and validation

Known database transport/timeouts return503 search_unavailable; unexpected failures go through the safe central500 handler. No raw database error, query value, cursor, secret, payload or stack appears in responses/logs. ProblemDetails errors contain code and traceId. Request duration metrics/logs use bounded status classes and route templates. Caller cancellation propagates rather than being used as permission for fallback reads.

Unit tests cover strict cursor authentication/binding/expiry/retirement/encoding, key validation/rotation, parameterized server SQL, bounded projection, controller result/error paths and serializer conversion. Host tests cover actual routes, OpenAPI response contracts, method rejection, millisecond wire timestamps, safe400/404/409/503/500 and startup validation. Real owned PostgreSQL fixtures cover literal wildcards/AND filters, description matching, empty results, closed details, both sorts/UUID ties, no gaps/duplicates after new ingestion, UTC midnight and pending-commit watermark boundaries. External tests do not contribute to unit coverage.

Stage7 provides local EXPLAIN/load evidence in performance/read-performance.md; monitoring exporter and persistent Docker provisioning remain later-stage work. No SLA or full-stack Angular integration is claimed. Existing B-tree/trigram migration is unchanged. See [Npgsql translations](https://www.npgsql.org/efcore/mapping/translations.html) for ILIKE escaping and row comparisons, and [EF Core keyset pagination](https://learn.microsoft.com/en-us/ef/core/querying/pagination).
