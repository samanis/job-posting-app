# Job posting API contract

The posting API has one endpoint: `POST /api/jobs`. It saves a new job and sends a `JobPostingCreated` message to RabbitMQ for the search API. There are no endpoints to read, update or delete jobs, and no authentication.

For the steps behind a successful post, see [POST workflow](post-workflow.md). For duplicate handling, see [Idempotency](idempotency.md).

## Request

- Content type: `application/json` (UTF-8).
- Maximum body size: 65,536 bytes.
- Header: exactly one `Idempotency-Key`.

```http
POST /api/jobs
Content-Type: application/json
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

### Idempotency key

An idempotency key is a unique ID the client creates for one submission. The client sends the same key on every retry. The API then knows the retry is not a new job.

- 1–128 characters: ASCII letters, digits, `_` or `-`. Keys are case-sensitive.
- A missing, empty, repeated or comma-joined header returns `400`. The API never generates a key itself.

### Fields

All seven fields are required. Text fields are trimmed first. Length limits apply after trimming.

| Field | Rule |
|---|---|
| `title` | String, 1–200 characters |
| `department` | String, 1–100 characters |
| `location` | String, 1–100 characters |
| `description` | String, 1–10,000 characters. Line breaks and inner spaces are kept. The text is stored as-is, not as HTML. |
| `salaryMin`, `salaryMax` | JSON number from 0 to 999999999.99, with at most two decimal places. `salaryMin` must be less than `salaryMax`. No currency is defined. |
| `closingDate` | String in `YYYY-MM-DD` format. It must be a real date. It must be later than today in the business time zone. |

The business time zone is `America/Toronto` by default. It is set by `JobPosting:BusinessTimeZone`. The browser form checks the date against the user's own local date, so the two can disagree near midnight.

### Which errors are 400 and which are 422

The API returns `400 Bad Request` when the request is badly formed:

- The body is not valid JSON or not a JSON object.
- It contains an unknown field, a wrongly cased field name or a repeated field.
- A field has the wrong JSON type, such as a salary sent as a string.
- A salary is too large to represent.
- `closingDate` is not a real date in `YYYY-MM-DD` format.
- The `Idempotency-Key` header is invalid.

The API returns `422 Unprocessable Entity` when the request is well formed but breaks a rule:

- A field is missing, `null` or blank.
- A text field is too long.
- A salary is out of range or has more than two decimal places.
- `salaryMin` is not less than `salaryMax`.
- `closingDate` is today or earlier.

Error messages never repeat the submitted values.

## Success: 202 Accepted

The API returns `202` only after three things succeed:

1. The job is saved in PostgreSQL.
2. RabbitMQ confirms it has stored the message.
3. The API records the publish time (`PublishedAt`) on the job.

The API does not wait for the search API. A `202` means the job will reach search, not that it is already searchable.

The body contains the saved job, a generated `id`, a UTC `createdAt`, and a fixed status and message:

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

Text values are returned trimmed. The API stores this response. A retry with the same key and body returns it again.

## Errors

Errors use Problem Details. This is a standard JSON error format (RFC 9457) with the content type `application/problem+json`. Every error body has a `traceId`. The same value is sent in the `X-Trace-Id` response header. Use it to find the request in the logs.

### Status codes

| Status | `code` | When it happens |
|---|---|---|
| `400` | – | The request or the key header is badly formed (see above) |
| `413` | – | The body is larger than 65,536 bytes |
| `415` | – | The content type is not `application/json` |
| `422` | – | A field breaks a validation rule (see above) |
| `409` | `idempotency_key_conflict` | The key was already used with a different job |
| `409` | `idempotency_in_progress` | Another request with the same key is saving the job right now |
| `503` | `publication_failed` | RabbitMQ did not confirm the message. The API deleted the saved job. |
| `503` | `publication_unresolved` | The job is saved, but the API cannot tell whether search will receive it |
| `503` | `dependency_unavailable` | The database is unavailable, or the API cannot tell whether the save succeeded |
| `503` | – | The API is shutting down (empty body) |
| `500` | – | An unexpected error |

Every `409 idempotency_in_progress` and every `503` with a `code` also sends `Retry-After: 1`. The client should then retry with the same key and the same body. After `publication_unresolved`, a retry never publishes again. It returns the same error while the unpublished job exists.

### Example: 422

Field errors are grouped by field name. Errors about the whole body use the name `$`.

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

A `400` has the same shape, with the title `Invalid request.`. An invalid key header is reported under the name `Idempotency-Key`.

### Example: 409 and 503

These errors add a `detail` message and a `code` at the top level:

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

A `500` has the title `An unexpected error occurred.` and asks the user to contact support with the trace ID. No error response contains stack traces, exception messages or connection details.

The posting app treats a `202` as success. It shows the saved job exactly as the API returned it.
