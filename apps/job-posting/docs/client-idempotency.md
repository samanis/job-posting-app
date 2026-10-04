# Client submission identity and recovery

Stage 4 provides a feature-local PostingAttemptStore. It has no HTTP dependency and does not dispatch requests. Stage 5 now connects its permission/state to transport and form controls through the page-scoped PostingWorkflow service.

## Workflow integration

- Call begin(normalizedPayload) once for a new submission. A returned attempt is permission to dispatch its key/payload; null means no request is allowed. Snapshot persistence is synchronous and completes before permission is returned. A successful begin immediately moves to in-flight, preventing subsequent concurrent clicks from obtaining permission.
- editingLocked is true for in-flight, unknown, pending, throttled, conflict, saved, or recovery-blocked states. Only idle and definitely rejected attempts permit editing/new submission. Stage 5 must bind this to the form, not merely disable a button.
- Call settle(dispatchedKey, outcome) for the result. Responses with absent/mismatched keys or non-in-flight attempts are ignored. validation maps to rejected; other outcome kinds map directly to lifecycle status. Definite rejection permits a new attempt/key even for an unchanged payload. Uncertain outcomes never permit begin.
- Call retry() only after an explicit user action. It takes no editable draft, so it returns the exact frozen original payload and key. Only unknown, pending, and throttled attempts can retry. Throttle deadlines use API_CLOCK and block early retry. Retry re-persists in-flight before returning dispatch permission. No timers or background retries are created by this store.
- Call postAnother() explicitly after confirmed success. It removes the saved attempt before enabling a fresh submission. Removal failure preserves success and blocks a new attempt. Failed saved-state persistence also preserves confirmed success in memory and blocks further posting, although refresh may recover an unknown prior attempt from the older stored record.
- Call resolveRecovery(true) only after the user explicitly confirms reconciliation of the prior server outcome. It is available only for a recovery problem or conflict. This is a caller obligation, not evidence that the client independently verified server state. Do not offer a blind discard-and-resubmit flow. Unresolved unknown/pending attempts cannot otherwise be reset.

## Storage and identity

ATTEMPT_STORAGE supplies read/write/remove adapters. The default uses sessionStorage key job-posting.attempt.v1. ATTEMPT_UUID supplies the key generator; the default uses crypto.randomUUID. Permission/availability exceptions are handled by the store. Stored records contain version 1, key, normalized payload, status, retryAt (nullable epoch milliseconds), and savedRecord (nullable API record).

Snapshots are copied, runtime-frozen, and compared by a deterministic ordered array of contract fields; input property order does not affect equality. Only contract payload fields are persisted. Text must already be trimmed, salaries finite/nonnegative with at most two decimals and strict bounds, and closing date a valid calendar date. A recovered payload may have an elapsed closing date: identity must remain unchanged during replay, and server behavior for such retries must honor the idempotency contract.

On startup the store validates JSON, schema version, key, payload, state, deadline, and saved-record shape. Persisted in-flight becomes unknown, because refresh or cancellation cannot prove server cancellation. No startup POST is sent. Malformed or unreadable data remains untouched and blocks new submissions. Write failures block dispatch; remove failures block new submissions. Raw exception/record contents are not used in user messages.

## Limits

Scope is one logical submission in one browser tab with refresh recovery. sessionStorage is not cross-device deduplication and does not survive every browser/tab lifecycle. Duplicated tabs may inherit a snapshot; independent tabs can create distinct attempts. The current protocol supports one outstanding attempt per tab. No automatic expiration or retention timeout is applied.

This client cannot guarantee at-most-once saves. The future backend must atomically enforce key/payload identity, serialize duplicates, replay saved responses, reject mismatches, and retain keys for the retry lifetime. Client unit tests prove state/permission behavior under adapters, not backend correctness. No backend or form/HTTP wiring was implemented in stage 4.

## Connected workflow (stage 5)

PostingWorkflow exposes computed editing/submitting/saved/rejected/pending/unknown/throttled/conflict/recovery-blocked state, message, savedRecord, canRetry and remainingSeconds. NewJobPage validates before calling submit, makes Signal Form controls read-only when editing is locked, disables repeat POST actions, maps server validation onto the form, and restores recovered payload values without sending a request. A saved API record is retained for the stage 6 confirmation component.

Only explicit clicks dispatch retries. A countdown polls the injectable API_CLOCK every 250 ms only while a throttle deadline is active, stops at the deadline, and is unsubscribed on page destruction. A missing/invalid delay produces a manual retry explanation. The store independently enforces the deadline when retry dispatch permission is requested. Page teardown cancels its subscription and marks a still-active attempt unknown. Persisted in-flight also recovers as unknown if teardown cannot run, e.g. abrupt refresh.

Named key mismatch conflicts display a distinct explanation. Recovery/conflict reset requires checking an explicit prior-server-outcome reconciliation checkbox before removing storage; this is user attestation and does not replace backend verification. There is no automatic reset or fresh-key retry. Confirmed saved state survives failed persistence/cleanup and cannot be submitted again. Stage 6 now provides saved-record details and the explicit post-another UI. Clearing failure keeps the confirmation visible; success resets the form and focuses title.
