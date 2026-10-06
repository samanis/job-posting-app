# Idempotency: how duplicate submissions are handled

Double-clicks, timeouts and retries can send the same job twice. An idempotency key makes sure each submission creates at most one job. The client creates a unique key for each new job and sends it in the `Idempotency-Key` header on every retry. The key rules are in the [API contract](api-contract.md#idempotency-key).

## Where the key is stored

There is no separate table for keys. The API stores a SHA-256 hash of the key in the job's own row, never the raw key. A unique index on this hash lets each key belong to only one job.

The row also stores a fingerprint: a hash of the seven fields after trimming. It shows whether a retry carries the same job. Field order, JSON formatting and outer spaces do not matter, and `10` equals `10.00`. Any other difference, including letter case, counts as a different job.

## What happens when a key arrives

The API first validates the fields. It then looks up the key before it checks the closing date.

| Situation | Result |
|---|---|
| New key | The API checks the closing date, saves the job and publishes it (see [POST workflow](post-workflow.md)) |
| Known key, same job, already published | `202` with the original stored response. Nothing is saved or published again. |
| Known key, different job | `409 idempotency_key_conflict` |
| Known key, same job, not yet published | `503 publication_unresolved`. The API never publishes it again. |
| Another request is saving the same key right now | `409 idempotency_in_progress` with `Retry-After: 1` |
| The database fails, or the API cannot tell whether the save succeeded | `503 dependency_unavailable` with `Retry-After: 1` |

Because the lookup comes first, a published job replays its `202` even after its closing date has passed. An invalid request does not use up a key, so the client can fix it and resend with the same key.

## Two requests at the same moment

Two requests with the same key may arrive together, even on different API instances. The unique index lets only one insert succeed. The other request reads the saved job and gets a result from the table above. If the first insert has not committed within 3 seconds, the other request gets `409 idempotency_in_progress`. Only the request that inserted the job publishes it.

## When the save outcome is unknown

If the connection fails during a commit, the API reads the row once more. If the job is there, it returns `503 publication_unresolved` and does not publish. If not, it returns `503 dependency_unavailable`, and the client should retry with the same key and body.

## How long keys last

A key lasts as long as its job. There is no expiry and no cleanup job.

When publishing fails, the API deletes the saved job, and the key with it. This undo step is called compensation. A retry with the same key then creates the job again. If RabbitMQ did store the first message, search may show the job twice (see [POST workflow](post-workflow.md#known-weak-spots)). A job left unpublished after a crash keeps its key, and retries return `503 publication_unresolved`.
