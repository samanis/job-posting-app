# Proposed job posting API contract

This document specifies client integration expectations. No API, persistence, or backend idempotency has been implemented or verified. Coordinate these decisions with the future .NET 10 API.

## Request

POST `/api/jobs`, `Content-Type: application/json`, `Idempotency-Key: <caller-supplied unique attempt key>`.

```json
{
  "title": "Engineer",
  "department": "Engineering",
  "location": "Toronto",
  "description": "Build software",
  "salaryMin": 100000,
  "salaryMax": 150000,
  "closingDate": "2027-02-28"
}
```

The workflow must pass an immutable normalized CreateJobRequest and its existing attempt key. The transport never generates keys or edits payloads. Its Observable is cold: each subscription sends one request, PostingWorkflow subscribes once per permitted dispatch and owns concurrency protection. No interceptor or automatic retry repeats this mutation.

Proposed validation: trim required text and reject whitespace-only values; salaries are finite, nonnegative decimal numbers with at most two decimal places and minimum strictly below maximum. Currency is unspecified. Closing date is a valid YYYY-MM-DD calendar date strictly later than today's local calendar date. The client implements form validation; the backend must independently validate.

## Saved record

Proposed 201 creation or 200 idempotent replay returns every request field plus nonblank string `id` and ISO `createdAt` with timezone, e.g. `2026-10-04T15:00:00Z`. Any other successful 2xx with a complete valid record is also recognized, except 202. Runtime checks reject missing/blank strings, nonfinite/negative salaries, reversed/equal bounds, invalid calendar dates, and invalid timestamps. Additional response fields are permitted. An old closing date may be valid on replay, so transport validation does not reject it for being in the past.

The returned record is authoritative; confirmation must display it rather than reconstructing the request. 202 means accepted/pending even if a record is present. 204 or incomplete/malformed 2xx, including HttpClient JSON parsing failures, is unconfirmed. No status endpoint is assumed.

## Outcomes

PostingOutcome is a discriminated union keyed by `kind`:

| HTTP/transport result | Client kind | Data and workflow behavior |
| --- | --- | --- |
| Complete saved 2xx except 202 | saved | Actual record and HTTP status; confirm completion |
| 202 | pending | reason accepted; retain attempt |
| Empty/malformed 2xx | unknown | reason invalid-success; retain attempt |
| 400/422 with usable errors | validation | fieldErrors, formErrors, status; display messages |
| 400/422 without usable errors | rejected | safe fallback and status |
| 409 code idempotency_in_progress | pending | reason in-progress; later manual same-attempt retry |
| 409 code idempotency_key_conflict | conflict | reason key-mismatch; explicit resolution, no fresh-key retry |
| Other 409 | conflict | reason general; retain context for resolution |
| 429 | throttled | retryAt epoch milliseconds or null; delay/manual same-attempt retry |
| Other 4xx | rejected | generic safe text and status |
| 5xx | unknown | reason server; retain exact attempt |
| Network/status 0 | unknown | reason network; server completion is uncertain |
| Timeout | unknown | reason timeout; server completion is uncertain |
| Other unexpected error/status | unknown | reason unexpected; safe fallback |

No dedicated 401/403 handling, authentication headers, redirects, or authorization behavior. These statuses fall through generic 4xx handling. Transport returns outcomes; it does not render messages, control form state, persist attempts, or retry them.

## Validation errors

Proposed ASP.NET-style Problem Details:

```json
{
  "title": "Validation failed",
  "status": 422,
  "errors": {
    "Title": ["Job title is required."],
    "SalaryMin": ["Minimum must be less than maximum."],
    "General": ["The posting could not be accepted."]
  }
}
```

Known names title, department, location, description, salaryMin, salaryMax, closingDate map case-insensitively, including PascalCase/camelCase. Multiple aliases merge messages. Unknown field messages become form-level messages. Only nonblank strings in message arrays are retained; malformed entries are ignored, and absent usable messages produce a safe fallback. General title/detail fields and raw diagnostic bodies are not displayed. Components render messages as text, never HTML.

Proposed 409 bodies use a top-level `code` string. Nested extension formats must be agreed before integration.

## Timing and Retry-After

POSTING_TIMEOUT_MS defaults to 15000 ms. Finite overrides clamp to 1000-60000 ms; nonfinite overrides use the default. RxJS timeout bounds the full response wait and cancels the client subscription, which does not prove the server canceled the save.

API_CLOCK is an injectable epoch-millisecond clock. Retry-After accepts nonnegative integer seconds (including zero) or a valid IMF-fixdate HTTP date. Future dates yield their absolute deadline; elapsed dates yield now. Missing, malformed, negative, fractional, invalid calendar/weekday, and unsafe overflow values yield null. Null means there is no reliable delay: the UI explains throttling and permits intentional manual retry without inventing a server deadline. Other obsolete HTTP-date formats are not currently supported; align this with the API.

## Backend obligations and unresolved integration agreements

The future backend must atomically associate a key and request fingerprint with at most one job, serialize concurrent duplicates, replay the saved response, and reject key/payload mismatches. Key retention must cover the client's retry lifetime. Do not expire unresolved client attempts automatically before retention is agreed. Retrying an uncertain outcome is safe only when the server honors this contract.

The client uses sessionStorage for recovery in one tab; independently entered submissions in different tabs/devices are not deduplicated by client code. Response DTO naming, error codes, timezone/closing-date semantics, salary precision/currency, retention lifetime, and actual API origin remain integration agreements. Client fixtures establish expected behavior, not proof of a real backend's correctness.

## Implementation references

- HttpClient full responses and error handling: https://angular.dev/guide/http/making-requests
- Angular HTTP test utilities: https://angular.dev/guide/http/testing

## Implemented client lifecycle

PostingWorkflow acquires a persisted attempt before dispatch, uses its caller key and payload unchanged, and subscribes once. It renders outcomes through the page, preserves unresolved snapshots for explicit same-key retries, and displays the actual saved API record. Definite validation/client rejection permits a corrected attempt with a new key. Pending/unknown/throttled outcomes keep the original identity; key conflicts require explicit reconciliation, never silent key replacement. Post another job explicitly clears confirmed saved state before a fresh attempt. Corrupt/unavailable sessionStorage blocks posting. No request is sent automatically after refresh.

Backend key retention remains unspecified; the client never automatically expires unresolved attempts. The server must replay the original result before applying changed current-date validation to an already-completed key. A stored payload may have an expired closing date by retry time. Agree how pending processing resolves and how outcomes can be reconciled before deploying, since no lookup/status endpoint is presently defined. Browser tab storage is not durable cross-device recovery.
