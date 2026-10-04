# Stage 2: API contract and response handling

Read `prompts/job-posting-angular/shared-requirements.md` first. Stage 1 must exist; implement only this stage.

## Tasks

1. Write `apps/job-posting/docs/api-contract.md` from the shared contract, marking every backend expectation as proposed. Define request, saved record, validation Problem Details, and a discriminated client outcome type.
2. Implement an injectable HttpClient service using `/api/jobs`, a caller-supplied immutable payload, and a caller-supplied idempotency key. Return typed outcomes without creating keys, mutating form state, or automatically retrying.
3. Inspect full HTTP responses. Recognize 200/201 with a runtime-validated complete saved record. Define safe handling for other 2xx: a complete saved record can indicate success except 202, which remains pending; an empty/malformed body remains unconfirmed.
4. Implement safe response classification: 400/422 validation or generic client error, named 409 cases, other conflicts, 429, generic remaining 4xx, 5xx, network failures, and timeouts. Preserve pending/unknown distinctions. Add a bounded configurable request timeout.
5. Normalize known server field names. Preserve unknown messages as form-level errors; never assume response bodies have the advertised shape. Render messages as text and keep transport details out of user messages.
6. Parse Retry-After seconds and HTTP dates using an injectable clock. Invalid, negative, or elapsed values must have explicit behavior. No authentication handlers.

## Acceptance and tests

- HttpTestingController tests assert URL, method, exact body, and Idempotency-Key header.
- Cover successful saved responses, replay, 202, 204, malformed success, valid/malformed validation, conflict codes, throttling, other 4xx, 5xx, network failures, and timeout.
- Cover malformed bodies and all Retry-After parsing branches deterministically.
- No fake backend behavior is presented as a guarantee; do not add a global automatic retry interceptor.
- Run relevant tests and build.
