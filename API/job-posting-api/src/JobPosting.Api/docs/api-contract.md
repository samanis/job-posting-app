> Stages 1-6 implement this contract using job-row idempotency and direct RabbitMQ publication; no ledger, outbox or recovery dispatcher.


# Job posting API contract

Stage 2 implements the DTOs, strict JSON reader, independent normalization/validation, temporal validation, fingerprinting and Problem Details factories described here. Stages 3–4 add PostgreSQL persistence and [durable idempotency coordination](idempotency.md). Stage 6 implements POST and publication/compensation. See [POST workflow](post-workflow.md).

## Request and validation

`POST /api/jobs`, `Content-Type: application/json`, one `Idempotency-Key` header.

```http
Idempotency-Key: 3340fd95-4f42-4df0-86aa-0cfb57a862fb
```

```json
{
  "title": "Engineer",
  "department": "Engineering",
  "location": "Toronto",
  "description": "Build software",
  "salaryMin": 100000.00,
  "salaryMax": 150000.00,
  "closingDate": "2028-02-29"
}
```

The header is an opaque, case-sensitive value of 1–128 ASCII letters, digits, `_` or `-`. Missing, empty, comma-combined or multiple values are invalid; no fallback key is generated, trimmed or silently replaced. The job row retains a unique key digest for its lifetime. Keys are not authentication, and distinct keys are not content-based deduplication.

All seven fields are required. Missing/null fields and blank text are semantic errors (422); nullable salary DTOs distinguish absent values from a legitimate zero. The JSON reader requires a single object, exact camelCase field names and supported JSON types. Unknown/case-variant and duplicate fields are rejected (400), a documented Stage 2 choice to avoid ambiguous payload fingerprints. Root arrays/null, malformed JSON (including invalid Unicode surrogate escapes), numeric strings, booleans/objects where text/numbers are expected, non-JSON NaN/infinity and decimal overflow are 400. Errors never echo submitted values or unknown property names.

| Field | Rule |
| --- | --- |
| `title` | Trim leading/trailing whitespace; 1–200 UTF-16 code units |
| `department` | Trim; 1–100 UTF-16 code units |
| `location` | Trim; 1–100 UTF-16 code units |
| `description` | Trim; 1–10,000 UTF-16 code units; retain internal spaces/newlines, case and literal plain text, including HTML-like characters |
| `salaryMin`, `salaryMax` | Decimal JSON numbers from 0 through 999999999.99 inclusive; at most two decimal places; minimum strictly less than maximum |
| `closingDate` | String containing a real date in exactly `YYYY-MM-DD`; for new work it must be strictly after today's date in the configured business timezone |

Text limits are measured **after trimming**, using .NET string length, not byte length or grapheme count. No HTML rendering/sanitization or Unicode normalization is performed; consumers must display description as text. These lengths and salary bounds are the documented implementation assumptions, not extra requirements attributed to the PDF. Currency remains unspecified.

Salary precision is checked on the original JSON token independently of decimal conversion, so parsing cannot hide extra fractions. For exponent notation, fractional scale is mantissa fractional digits minus exponent: `1e1` and `10.00` are valid equivalents; `1e-3`, `10.001` and even `10.000` have excessive scale and are 422. `10.000e1` is valid (100.00). Very small fractions are rejected rather than accepted as rounded zero; overflow is 400. Direct typed validation also checks decimal scale. No floating-point arithmetic is used. The endpoint uses `CreateJobRequestReader`; permissive MVC/default numeric-string binding does not establish this contract.

A malformed/nonexistent calendar date or datetime string is 400; missing/null date is 422. Closing date is never converted to UTC. Default business timezone is `America/Toronto`, startup validated; the validator converts injectable `TimeProvider.GetUtcNow()` into this zone only to determine today's calendar date. The browser currently validates against its own local date; users outside this timezone can disagree with the server near midnight. Later client integration must explain/align this rule.

## Preparation, temporal validation and replay

The request workflow uses these boundaries in order:

1. Validate the header with `IdempotencyKeyValidator.TryValidate`.
2. Read JSON with `CreateJobRequestReader.Read`. A non-null `ErrorStatus` is 400 or 422; do not continue with a failed result. Mixed syntax/precision errors use 400 if any syntax error exists.
3. Normalize and validate non-temporal fields with `JobRequestValidator.Normalize`. Failure yields 422 field errors. Success returns an immutable `NormalizedJobRequest`.
4. Compute `JobRequestFingerprint.Compute`, using `JobRequestFingerprint.Version` (currently 1), then resolve the matching saved job by digest before applying today's date rule. A mismatched fingerprint conflicts.
5. Only for a **new key**, call `NewJobTemporalValidator.ValidateNew`. Its closing-date error is 422. A matching saved request bypasses current-date validation: published rows replay 202, while unpublished rows remain unresolved without recovery or republication.

Normalization and fingerprinting never read the clock. Their success alone is not acceptance; all new requests still require temporal validation and the durable workflow. JSON metadata/ordering and leading/trailing text whitespace do not participate in fingerprint identity. Internal whitespace, case, Unicode code-point differences and each of the seven fields do.

Canonicalization version 1 produces a UTF-8 JSON array in this fixed order:

```json
[1,"Engineer","Engineering","Toronto","Build software","100000.00","150000.00","2028-02-29"]
```

The leading number is the canonicalization version. Salary entries are invariant decimal **strings with exactly two places** inside the canonical representation only; wire request/response salaries remain JSON numbers. Date is invariant `yyyy-MM-dd`. System.Text.Json's default escaping keeps delimiters, quotes, Unicode and newlines unambiguous. The SHA-256 digest of these exact bytes is a 64-character lowercase hex fingerprint. Equivalent 10 and 10.00 produce equal fingerprints, independent of process culture or property order. Persist version alongside the hash; changing canonicalization requires an explicit version/migration/replay strategy, not silently rehashing retained keys.

## Acceptance: 202 with the complete authoritative saved record

Return 202 **only after** (a) the PostgreSQL transaction commits the job and its idempotency metadata, (b) RabbitMQ confirms the directly published event and mandatory routing produces no return, and (c) PublishedAt is durably recorded on the same job. PostgreSQL save alone, an unconfirmed send or a confirmation with an unroutable return is insufficient.

```json
{
  "id": "b923c8d1-a4ee-4e2a-97a9-567b8e2fabdc",
  "createdAt": "2027-01-01T05:00:00+00:00",
  "title": "Engineer",
  "department": "Engineering",
  "location": "Toronto",
  "description": "Build software",
  "salaryMin": 100000.00,
  "salaryMax": 150000.00,
  "closingDate": "2028-02-29",
  "status": "accepted",
  "message": "Your job posting has been saved and sent for processing. It may take a few moments to appear in search results."
}
```

`SavedJobRecord` contains all seven normalized fields plus a generated string UUID and UTC `createdAt`. `SavedJobRecord.Create` accepts the persistence-generated identity/time and normalizes the supplied timestamp to UTC. `JobAcceptedResponse.FromSaved` copies this authoritative record at the top level, adding fixed status/message. It does not itself verify the acceptance gate. Persist the original response snapshot for a stable 202 replay (same identity, timestamp, fields and message); do not regenerate it or republish a known-completed event. Decimal JSON formatting may omit redundant zeroes without changing numeric meaning.

The message means broker-confirmed acceptance for asynchronous search projection, not verified search visibility. Do not invent a Location/status URI or return a body-less 202. There is no job GET/status, update/delete or search endpoint in this phase. Broker redelivery and recreation after compensating deletion can duplicate event delivery; a future search consumer must deduplicate event IDs.

## Error responses

Use `application/problem+json`, camelCase field names, safe server-generated messages, top-level `traceId` and matching `X-Trace-Id`. Code values are top-level Problem Details extensions, not nested in an `extensions` object. `JobApiProblems.Validation` and `.Failure` build these contracts; the endpoint is responsible for actual HTTP status/content type/headers. Central exception handling already supplies generic 500 responses.

### 400: invalid JSON/header/shape/type/calendar date

```json
{
  "type": "https://www.rfc-editor.org/rfc/rfc9110.html#section-15.5.1",
  "title": "Invalid request.",
  "status": 400,
  "errors": { "salaryMin": ["The field must be a representable decimal JSON number, not a string."] },
  "traceId": "trace-example"
}
```

An invalid header uses `errors: { "Idempotency-Key": ["Provide exactly one key of 1–128 ASCII letters, digits, underscore or hyphen."] }`. Malformed JSON/root/unknown fields use `$`. The reader does not expose parsing exceptions, submitted values, stack traces or infrastructure details.

### 422: required fields, length/range/precision/order or new-date rule

```json
{
  "type": "https://www.rfc-editor.org/rfc/rfc9110.html#section-15.5.21",
  "title": "Validation failed.",
  "status": 422,
  "errors": {
    "salaryMax": ["The maximum salary must be greater than the minimum salary."],
    "closingDate": ["The closing date must be later than today in the configured business time zone."]
  },
  "traceId": "trace-example"
}
```

### 409: same key with a different normalized payload

```json
{
  "type": "https://www.rfc-editor.org/rfc/rfc9110.html#section-15.5.10",
  "title": "Idempotency key conflict.",
  "status": 409,
  "detail": "This key is associated with a different job posting. Resolve the existing attempt before submitting another.",
  "code": "idempotency_key_conflict",
  "traceId": "trace-example"
}
```

### 409: another owner is processing the same key

Supply bounded integer-seconds `Retry-After` (`Retry-After: 1`), then:

```json
{
  "type": "https://www.rfc-editor.org/rfc/rfc9110.html#section-15.5.10",
  "title": "Job posting is being processed.",
  "status": 409,
  "detail": "Wait for Retry-After, then retry the same key and payload.",
  "code": "idempotency_in_progress",
  "traceId": "trace-example"
}
```

### 503: committed job, publication or confirmed-state recording unresolved

Supply `Retry-After` (1 second), then:

```json
{
  "type": "https://www.rfc-editor.org/rfc/rfc9110.html#section-15.6.4",
  "title": "Publication is pending.",
  "status": 503,
  "detail": "The job has been saved, but publication is unresolved. Retain the same key and payload; manual investigation may be required.",
  "code": "publication_unresolved",
  "traceId": "trace-example"
}
```

There is no automatic recovery or republication. Confirmed-before-status failure retains the job; cleanup failure may leave its state uncertain. Manual investigation may be required. A publication exception with successful deletion instead returns 503 `publication_failed`; a queued message may still exist after lost acknowledgement. Neither failure returns 202.

### 503: database unavailable or commit outcome uncertain

Supply `Retry-After`, then:

```json
{
  "type": "https://www.rfc-editor.org/rfc/rfc9110.html#section-15.6.4",
  "title": "A required dependency is unavailable.",
  "status": 503,
  "detail": "The outcome may be uncertain. Wait for Retry-After, then retry the same key and payload.",
  "code": "dependency_unavailable",
  "traceId": "trace-example"
}
```

### 500: unexpected exception

```json
{
  "type": "https://www.rfc-editor.org/rfc/rfc9110.html#section-15.6.1",
  "title": "An unexpected error occurred.",
  "status": 500,
  "detail": "The request could not be completed. Contact support with the trace identifier.",
  "traceId": "trace-example"
}
```

Server logs contain safe failure types and correlation/identity context; raw exception messages/stacks are suppressed. Client timeouts/disconnects do not prove a save failed; retry the same key/payload for unresolved work. This demo phase has no authentication/authorization and is not an authenticated public production service.

## Angular integration remains a later task

The current posting client's [contract](../../../../../apps/job-posting/docs/api-contract.md) and transport explicitly treat **every 202** as pending/unconfirmed, even when a complete saved record is supplied. This API's broker-confirmed 202 instead resolves creation and must display the authoritative saved record, clear the resolved attempt and explain eventual search visibility. Updating that client behavior, its tests and timezone communication is a separate integration task; this stage does not edit Angular files or connect the form to this API.

## Verification and implementation references

Run `dotnet run --project tools/CoverageGate` from `API/job-posting-api`. The isolated unit suite covers JSON shape/type errors, all required fields/limits, raw precision and overflow, deterministic canonicalization, all seven fingerprint fields, response/problem contracts and timezone/leap/midnight boundaries. In-process host tests separately verify startup/DI, POST responses, strict input rejection and safe exception mapping. Stage 3 adds PostgreSQL persistence and separately measured real-database tests; see the [persistence guide](persistence.md). Real PostgreSQL/RabbitMQ workflow tests verify success, replay, concurrent duplicates, compensation and status failure windows.

- [System.Text.Json decimal-token conversion](https://learn.microsoft.com/en-us/dotnet/api/system.text.json.jsonelement.trygetdecimal?view=net-10.0): checks JSON numeric type and representability; raw precision still requires independent validation.
- [System.Text.Json date/time support](https://learn.microsoft.com/en-us/dotnet/standard/datetime/system-text-json-support): date/timestamp serialization support; the request reader separately enforces exact date-only syntax.

See the [client integration handoff](../../../docs/client-integration-handoff.md) and [final verification/runbook](../../../docs/verification-and-handoff.md).
