# Stage 4: job-row idempotency and minimal concurrency

Read `API/job-posting-api/shared-requirements.md` and `requirements-review.md` first. Inspect completed stages and applicable AGENTS.md. Implement only this stage; preserve unrelated work. Existing Stages 3-4 use the superseded ledger/outbox design: refactor them to these revised requirements before implementing dependent stages. Do not mistake earlier passing checks for verification of the revised design.

## Tasks

1. Validate one caller-provided opaque Idempotency-Key (1-128 ASCII letters/digits/underscore/hyphen); compute case-sensitive SHA-256 UTF-8 digest. It identifies a submission attempt, not content deduplication or authorization.
2. Normalize/fingerprint all seven fields and look up the jobs row by digest. Remove ledger-specific contracts, scope and references. Store versioned fingerprints and original response on the job itself.
3. For matching existing jobs, compare fingerprint before future-date validation. Different payload returns conflict; matching published job replays the original saved response without inserting or publishing. Matching unpublished job returns bounded in-progress/unresolved failure and never automatically republishes.
4. Validate a future closing date only for new work. Insert exactly one job using PostgreSQL unique digest constraint. On a competing duplicate-key insert, dispose the failed transaction/context and read the winning job through a fresh context. No reservation service, in-memory locks or distributed leases.
5. Keep lookup/insert waits bounded. Map contention to idempotency_in_progress with Retry-After; missing/unavailable uncertain-commit reconciliation remains dependency_unavailable. Perform at most a diagnostic fresh read, not whole-request retry or recovery orchestration; never claim absence proves rollback.
6. Retain key/fingerprint for the lifetime of a remaining job. Invalid requests consume no key. Compensation deletion removes the job and its key, so subsequent recreation is possible; document this exception and its duplicate-delivery risk. Unsupported canonicalization versions fail safely.
7. Test independent-host equal/conflicting races, numeric equivalence, published replay after expiry, unpublished duplicate behavior, cancellation and uncertain commit. No HTTP acceptance yet without a real publisher.

## Acceptance and checks

- Run the mandatory 100% line/branch/method coverage gate with complete authored-source inclusion and unchanged justified generated-code exclusions. Report isolated unit/host coverage separately from external integration evidence.

- One job exists per retained key; no ledger, outbox or publication recovery is implemented.
- Matching duplicates never create/publish another job. No background retry or same-key republishing of unresolved rows.
- Tests demonstrate database uniqueness rather than relying on process-local test state.

Run appropriate build/tests and record actual results in `API/job-posting-api/ai-log/job-posting-api-work-notes.md`. Report changed files, assumptions and blockers. Do not commit, push or continue into later stages automatically.
