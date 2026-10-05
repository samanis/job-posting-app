# Stage 2: contracts and server-side validation

Read `API/job-posting-api/shared-requirements.md` and `requirements-review.md` first. Inspect completed stages and applicable AGENTS.md. Implement only this stage; earlier numbered stages must be complete. Preserve unrelated work.

## Tasks

1. Define create DTO, authoritative saved-record DTO, stable 202 acceptance body, validation Problem Details and error-code contract from shared-requirements.md. Use explicit nullability to detect missing salary zero versus legitimate zero.
2. Implement independent normalized validation for all seven fields, text lengths, salary decimal bounds/precision, min < max, date-only parsing and future date in configured business timezone. Add TimeProvider-based tests around midnight/leap days and invalid timezone startup.
3. Implement deterministic versioned canonicalization/fingerprinting: normalized strings, exact decimals and dates in fixed order. Preserve description as plain text. Never rely on dictionary order, culture or floating-point rounding.
4. Separate syntax/normalization/fingerprint generation from new-request temporal validation so later idempotent replay can bypass changed date rules safely. No missing/default fields silently accepted.
5. Write service docs/api-contract.md with precise 400/422/409/503/202 examples and the complete saved-record response. Explicitly explain why current Angular handling of 202 needs a later integration task; do not edit it now.

## Acceptance and checks

- Run the mandatory coverage gate: 100% lines, branches and methods for all authored production code, with source completeness and exclusions checked as required by shared-requirements.md. Keep unit/host coverage and external integration evidence accurately distinguished.

- Unit tests cover missing/null/whitespace fields; all length, amount and precision limits; wrong JSON types; invalid dates; timezone boundaries.
- Equivalent 10 and 10.00 requests produce equal fingerprints, different payloads do not; cultures do not change hashes.
- Contract documents every response and the strict confirm gate. No writes or broker code yet.

Run appropriate build/tests and record actual results in `API/job-posting-api/ai-log/job-posting-api-work-notes.md`. Report changed files, assumptions and blockers. Do not commit, push or continue into later stages automatically.

