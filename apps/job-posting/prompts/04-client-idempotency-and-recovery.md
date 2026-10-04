# Stage 4: Client idempotency and recovery

Read `apps/job-posting/prompts/shared-requirements.md` first. Stages 1–3 must exist; implement only this stage.

## Tasks

1. Implement a small feature-local attempt store with injected UUID generation and sessionStorage access. An attempt contains a unique key, an immutable normalized payload snapshot, and a versioned lifecycle status.
2. Create one key per logical submission. Compare normalized payloads deterministically; retries reuse the stored key and exact original payload, never the latest form values.
3. Enforce a synchronous in-flight lock so concurrent clicks cannot start multiple requests. Persist the attempt before dispatch; if persistence is unavailable, block dispatch with a clear recoverability message rather than silently claiming refresh protection.
4. Recover pending attempts after refresh. Treat a persisted in-flight attempt as unknown, since the original server request may have completed. Do not automatically resubmit on startup.
5. Keep pending/unknown attempts and their payload locked from editing until their outcome is resolved. Do not generate a new key after a network error, timeout, 5xx, 202, malformed success, or in-progress conflict.
6. For definite rejection, permit correction as a new logical attempt with a new key. For key/payload conflict, preserve context and require explicit resolution; never silently replace the key. After confirmed success, require an explicit “Post another job” action for a new attempt.
7. Validate stored data and schema versions. Never blindly clear malformed storage and then enable a fresh submit: a prior attempt might have committed. Enter a recovery-blocked state with an explicit user action and explanation. Document tab scope and retention limitations.

## Acceptance and tests

- Assert deterministic payload snapshots, fresh keys for fresh attempts, stable keys for retries, double-submit locking, and correct lifecycle transitions.
- Cover refresh recovery, storage read/write/remove failures, malformed/version-mismatched records, and unavailable UUID generation.
- No attempt expires automatically; no background retry or cross-tab guarantee is introduced.
- Store tests use injectable adapters, not nondeterministic globals. Run tests and build.
