# Stage 5: Submission workflow and retries

Read `prompts/job-posting-angular/shared-requirements.md` first. Stages 1–4 must exist; implement only this stage.

## Tasks

1. Connect the form, API service, and attempt store through a small signal-based feature state service. Define explicit states: editing, submitting, saved, rejected, pending/unknown, throttled, conflict, and recovery-blocked. Derive button availability and messages with computed state.
2. Validate before obtaining an attempt, acquire the lock synchronously, save its snapshot, and dispatch once. Handle teardown deliberately: client cancellation does not prove server cancellation. An interrupted active attempt remains recoverable as unknown.
3. On complete saved response, retain the API record for stage 6 and transition to saved. Do not reconstruct success from submitted values.
4. Apply field and form messages for 400/422. Keep draft values. Allow corrected definite rejections to begin a new attempt. Handle unrelated 4xx with clear messages and intentional transition rules.
5. Display named conflict outcomes distinctly. Pending/unknown outcomes keep their original key and payload. Offer only an explicit same-attempt retry where appropriate; never automatically POST again.
6. For 429, show a retry delay and disable manual retry until its valid deadline. Use a testable clock/timer and clean up timers on destruction. Invalid Retry-After uses a clear manual-retry fallback.
7. Handle storage cleanup failures after success without changing saved into failed or enabling accidental duplicate submission. If server rejection means a key was consumed, a correction uses a new key as specified in the contract.

## Acceptance and tests

- Component/service tests cover each response class, rendered messages, retained values, server field mapping, and retry availability.
- Rapid clicks produce one POST; retry after uncertain outcome produces the exact same body and header; correction after rejection creates a fresh key.
- Refresh recovery does not POST automatically. Unresolved payloads cannot be changed and resubmitted under a new identity.
- Test delayed responses, teardown, timer cleanup, throttle boundaries, and persistence failures.
- No authentication behavior or backend implementation. Run tests and build.
