# Posting client integration handoff

The posting API is implemented; connecting the Angular client is a separate task. The existing client treats every 202 as pending/unconfirmed. Change that interpretation when integrating this API: a full-record 202 resolves creation and confirms the authoritative saved record. Search visibility is asynchronous and has not been verified. No consumer acknowledgment is required.

## One key per logical submission

Generate an opaque UUID before the FIRST POST and send it in `Idempotency-Key`. Keep the original key and submitted payload together while the outcome is uncertain. Repeated requests must use that same pair; do not regenerate a key on a timeout or silently reuse a key for an edited form. Preserve the attempt across navigation/reload if the client supports resuming it.

The server accepts one case-sensitive key of 1-128 ASCII letters, digits, underscore or hyphen. It stores its SHA-256 digest on the job row and a separate fingerprint of the seven normalized fields. Distinct keys may create identical content. A key is not authentication.

On 202, display the returned ID, UTC createdAt and all seven saved fields, mark the attempt complete and explain that the posting may take a few moments to appear in search. Clear the active submission state; a genuinely new posting gets a new key. A repeated completed request returns the original response without publishing again, even after its closing date has passed or while the broker is unavailable. There is no public job GET, status URL or polling endpoint.

## Responses and uncertain outcomes

| Response | Client behavior |
| --- | --- |
| 400 / 422 | Show safe field errors. Correct validation errors; these do not create a job or consume a new key. |
| 409 `idempotency_key_conflict` | The retained key belongs to a different normalized payload. Resolve the original attempt before submitting a different posting. Do not blindly rotate keys. |
| 409 `idempotency_in_progress` | Another insert is still in progress. Respect Retry-After and retain the original key/payload. |
| 503 `publication_unresolved` | Retain the attempt and trace ID; manual investigation may be required. Repeating the request does not repair or republish a retained unpublished job. |
| 503 `publication_failed` | Publication failed and compensating deletion succeeded. Delivery may nevertheless have happened if a broker acknowledgment was lost. Resubmission can recreate the job and duplicate downstream delivery; do not add automatic retries. |
| 503 `dependency_unavailable`, 500 or transport timeout | The outcome may be uncertain. Retain the original key/payload and trace ID when available. Any explicitly offered repeat uses the original pair, not a new key. |
| 413 / 415 | Reduce the body size or use application/json; do not retry unchanged input. |

`Retry-After` is a delay hint, not a recovery guarantee. No background repair exists. Store/report safe `traceId` or `X-Trace-Id`; do not show raw server or infrastructure errors. Avoid implying that a failed HTTP response proves no message was delivered.

## Request boundaries

POST `/api/jobs` with UTF-8 `application/json`, the key header and exactly these fields:

```json
{
  "title": "Engineer",
  "department": "Engineering",
  "location": "Toronto",
  "description": "Build software",
  "salaryMin": 100000,
  "salaryMax": 150000,
  "closingDate": "2028-02-29"
}
```

All fields are required. Trim text before validation; limits after trimming are title 200, department/location 100 and description 10,000 UTF-16 code units. Internal whitespace and case remain significant. Render the description as plain text.

Salary values must be JSON numbers between 0 and 999999999.99, with at most two decimal places and minimum strictly less than maximum. Do not emit numeric strings or rounded invalid input; even a raw token `10.000` is rejected. Currency is unspecified. Closing date must be a real date in exactly YYYY-MM-DD, strictly after today in the configured business timezone for a new key. Default timezone is America/Toronto; browser-local today can disagree near midnight. Align the client with deployment configuration and explain that zone. Do not convert the date through a UTC timestamp. Existing matching requests bypass only the new-date rule, not structural validation.

Default body limit is 65,536 UTF-8 bytes. Unknown, duplicate and case-variant properties are rejected. See the [complete API contract](../src/JobPosting.Api/docs/api-contract.md) for examples and precision rules.

Use a same-origin development/reverse proxy or explicitly scope a future CORS change; no cross-origin integration has been implemented here. Authentication, search API/consumer/query database and client integration remain deferred. This handoff does not claim a working end-to-end search flow.
