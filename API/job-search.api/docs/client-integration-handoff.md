# Search client integration handoff

The API is implemented and verified independently. Angular was inspected, not modified or connected in this stage.

## HTTP contract

GET `/api/jobs` returns 200 `{items: [...], nextCursor: null|string}`. Summary fields are `id`, `title`, `department`, `location`, `salaryMin`, `salaryMax`, `closingDate`, `createdAt`. GET `/api/jobs/{id}` returns the same fields plus `description`; closed jobs remain accessible in detail. IDs are nonempty D-format UUIDs. Dates are `yyyy-MM-dd`; outgoing timestamps are UTC with exactly three fractional digits. Ingestion retains the source's seven-digit precision separately. Salaries are JSON numbers; currency is unspecified. Render descriptions as plain text.

Query names are case-sensitive: `q`, `department`, `location`, `limit`, `sort`, `cursor`. Unknown or duplicate parameters fail. Filters are trimmed; empty filters are omitted. Limits are 200/100/100 characters respectively; controls are rejected. Filters combine with AND; q matches a literal case-insensitive substring in title OR description, and the other filters match their corresponding fields. Limit defaults to 20 and accepts 1–50. Sort defaults to `newest` (createdAt descending, UUID descending); `closing-soon` orders closingDate ascending, then the same tie breakers.

Only jobs with closingDate strictly after the captured UTC calendar day appear in lists. Posting's Toronto date policy differs; neither timezone is mandated by the exercise. A successfully posted job becomes visible after broker delivery and search database commit. Search never returns 202: a not-yet-projected job is absent from the list and its detail returns 404.

| Response | Meaning / client action |
| --- | --- |
| 400 | Invalid query, UUID, or cursor; correct input. |
| 404 | Detail record not found. |
| 409, code `cursor_expired` | Authentic cursor expired or its supported version retired; explicitly start a new first page. |
| 503 | Search database temporarily unavailable; offer a manual retry. |
| 500 | Generic unexpected failure; use trace ID for diagnosis. |

Errors use safe ProblemDetails with diagnostic trace IDs; internal exceptions and submitted values are not exposed. Authentication and throttling are not implemented, so 401/403/429 policies remain separate work.

## Pagination and refresh

Cursors are opaque signed tokens bound to normalized filters, sort and limit, a fixed UTC day, ingestion watermark and original expiry. Default lifetime is 15 minutes. Continuations exclude jobs ingested after the first page, including concurrent inserts. Changing criteria requires removing the cursor and discarding the previous page chain. Expiry requires the same reset; retrying the expired token cannot recover it. To see new postings, explicitly request a fresh first page. Removing a signing key makes its tokens invalid (400), rather than expired (409). Replicas must share the configured key ring; see [key rotation](search.md).

## Existing Angular inspection

Inspected `apps/job-search` query-contract.ts, job-query-api.ts, query-response.ts, search-query.ts, query workflow/cache, proxy.conf.cjs, package.json and its proposed docs/api-contract.md. Valid returned data matches its routes, fields, sorts, numeric salaries and millisecond timestamp parser. Remaining differences and integration decisions:

| Finding | Required integration decision |
| --- | --- |
| Frontend contract document still describes a proposed backend. | Update it during frontend integration to the implemented contract. |
| Client accepts broader text IDs and compares detail response ID to requested ID case-sensitively. API accepts UUIDs and emits normalized lowercase UUIDs. | Use returned IDs verbatim or normalize UUIDs before comparison. |
| Client defensive field bounds exceed API limits. | Align documented limits; this does not reject valid API responses today. |
| Refresh resends the current cursor; expiry UI recognizes `cursor_expired`. | Reset route/state cursor explicitly on restart; discard old accumulated pages. |
| Client success cache lasts 30 seconds. | Explain additional visibility delay; a fresh first-page refresh must bypass stale cache as appropriate. |
| Client timeout defaults to 10 seconds; API's bounded database commands can collectively take longer. | Decide timeout/manual-retry UX under slow dependencies; do not add automatic retry or polling implicitly. |
| Client understands extra 410/422/429 responses that this API does not implement. | Do not treat these as implemented server guarantees. |

`npm start` explicitly loads proxy.conf.cjs. That proxy only enables `/api/**` when `JOB_SEARCH_API_URL` is set (Docker API http://localhost:5101, host profile http://localhost:5100). Bare `ng serve` has no automatic proxy configuration in angular.json. Production requires an explicit same-origin reverse proxy or separately scoped CORS configuration; this API currently has no CORS policy. Browser integration and a full UI → posting → broker → search flow were not verified here.
