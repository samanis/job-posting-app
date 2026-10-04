# Stage 3: Signal Form and validation

Read `apps/job-posting/prompts/shared-requirements.md` first. Stages 1–2 must exist; implement only this stage.

## Tasks

1. Implement the job form using Angular 22 Signal Forms, checking the installed stable APIs. Use signal/computed state and Angular template control flow.
2. Include title, department, location, description, salary minimum/maximum, and closing date with explicit labels, appropriate native input types, help text, and stable error IDs.
3. Implement the shared validation rules, including numeric precision and cross-field salary validation. Normalize a request payload independently from display values; do not convert blank salary inputs to zero.
4. Introduce a testable local-date clock. Compare date-only values as valid calendar dates. Reject invalid dates and today; permit tomorrow. Revalidate when submission is requested.
5. Show errors after interaction or attempted submission, associate them with controls, and focus the first invalid control on invalid submission. Provide an accessible error summary.
6. Provide an output/callback seam for a valid payload, but do not send HTTP requests in this stage. Support external field/form messages so stage 5 can display server errors. Clear stale field errors when that field changes without accidentally clearing unrelated errors.

## Acceptance and tests

- Cover blank and whitespace-only text, valid text, absent/nonfinite/negative/overprecision salaries, equality and reversed bounds, and valid salary ranges.
- Cover today/tomorrow, invalid dates, leap dates, year boundaries, timezone behavior, and a clock crossing midnight.
- Assert controls, rendered errors, focus, accessible associations, payload normalization, and external error behavior.
- Invalid submissions emit nothing; a valid submission emits the normalized payload once.
- No idempotency lifecycle or real POST is implemented yet. Run tests and build.
