# Duplicate protection in the Job Posting app

Double-clicks, timeouts and retries can create the same job twice. The app prevents this with an idempotency key: a unique ID for one submission. It travels in the `Idempotency-Key` header. If the API sees a key again, it returns the original result instead of saving a second job.

The code is in `src/app/features/job-posting/state` and `src/app/core/api/posting-response.ts`.

## Life of one submission

1. **Create.** The form passes validation. The app creates a key with `crypto.randomUUID()`.
2. **Store.** The app saves the key, the job details and a status in sessionStorage, under `job-posting.attempt.v1`. sessionStorage is browser storage for one tab that survives a refresh. This happens before sending. If it fails, nothing is sent.
3. **Send.** The app sends one `POST /api/jobs`. The form turns read-only and extra clicks do nothing. The request times out after 15 seconds.
4. **Settle.** The app classifies the response (table below) and stores the new status.
5. **Retry.** If the result is unclear, the user can click "Retry same submission". It resends the same key and the same stored details. The app never retries on its own.
6. **Clear.** After a save, "Post another job" deletes the record. The next job gets a new key.

On refresh, the app restores the record and its details. It sends nothing automatically. A request that was still in flight becomes "unknown", because the server may or may not have saved it. Leaving the page cancels the request and marks it "unknown" too.

## How each response is handled

| Response | What the user sees |
|---|---|
| Any 2xx with a complete saved job (including `202`) | The confirmation with the saved job |
| `202` without a complete job | "Not yet confirmed"; retry with the same key |
| Other 2xx without a valid job | Unknown result; retry with the same key |
| `400` or `422` | Errors next to each field (or a general message); the form unlocks and the next submit gets a new key |
| Other `4xx` (not `409` or `429`) | A general error; the form unlocks |
| `409` with code `idempotency_in_progress` | Still processing; retry the same submission later |
| Other `409` | Conflict; posting is blocked (see below) |
| `429` | A countdown from `Retry-After`; retry unlocks when it ends, or at once if the header is missing |
| `5xx`, network error or timeout | Unknown result; retry with the same key |

## Blocked submissions

Posting is blocked after a conflict, or when the stored record cannot be read or written. The user must first check what happened to the earlier submission. They then tick a confirmation box and click "Reset reconciled submission". There is no one-click "discard and resubmit", because that could create a duplicate.

## What it does and does not protect against

| Protected | Not protected |
|---|---|
| Double-clicks on "Post job" | Separate tabs or devices: each has its own storage and key |
| Retries after a timeout, network drop or server error | Closing the tab: the record is lost |
| Refreshing during a submission | Two people typing in the same job: the key marks a submission, not a job |

The client alone cannot guarantee one save. The API stores each key with its job and replays the original response. It returns `409` if a key comes back with different details. See section 3 of [DECISIONS.md](../../../DECISIONS.md).
