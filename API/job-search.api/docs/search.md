# Search API

The search API has two read-only endpoints. It reads only its own PostgreSQL database, which RabbitMQ messages from the posting API keep filled. See [DECISIONS.md](../../../DECISIONS.md) for the design.

## Endpoints

```text
GET /api/jobs?q=engineer&location=toronto&limit=20&sort=newest
200 {"items":[{"id","createdAt","title","department","location","salaryMin","salaryMax","closingDate"}],"nextCursor":"<token>"|null}

GET /api/jobs/{id}
200 {"id","createdAt","title","department","location","description","salaryMin","salaryMax","closingDate"}
```

- The list returns open jobs only, without descriptions.
- The detail endpoint returns any job, including closed ones.
- `closingDate` is `yyyy-MM-dd`. `createdAt` is UTC with milliseconds, such as `2026-10-06T14:05:09.123Z`.

## List parameters

| Parameter | Meaning | Limit |
|---|---|---|
| `q` | Text in the title or description | 200 characters |
| `department` | Text in the department | 100 characters |
| `location` | Text in the location | 100 characters |
| `limit` | Jobs per page | 1–50, default 20 |
| `sort` | `newest` (default) or `closing-soon` | — |
| `cursor` | Page token from the previous page | 2,048 characters |

- Text filters are case-insensitive "contains" matches. They combine with AND.
- Blank filters are ignored. `%` and `_` match literally.
- An unknown, repeated or out-of-range parameter returns `400`.

**Sorting.** `newest` puts the newest job first. `closing-soon` puts the earliest closing date first, then the newest. Job ID breaks any tie.

**Open job.** A job is open while its closing date is after today's UTC date. It leaves the list at midnight UTC on its closing date.

## Paging and cursors

The API uses **keyset paging**: each page starts right after the last job of the previous page. The database never skips over earlier rows, as `OFFSET` does, so late pages stay fast. There is no total count.

To get the next page, send `nextCursor` as `cursor` and keep the other parameters the same. A `null` `nextCursor` means the last page.

A **cursor** (page token) is an opaque string marking where the next page starts.

- **Signed.** It carries an HMAC-SHA256 signature: a code made from the token and a secret key. Any edit breaks it. The token is not encrypted.
- **Tied to the search.** It holds a hash of the filters, limit and sort. Other values are rejected.
- **Snapshot.** The first page fixes the UTC date and the newest job received. Later pages use the same snapshot. New jobs do not appear mid-session, so no job is skipped or repeated.
- **Expiry.** It expires 15 minutes after the first page by default (configurable from 1 to 60). Paging does not extend it.

| Cursor problem | Response |
|---|---|
| Edited, malformed, unknown key, or different filters | `400 invalid_cursor` |
| Authentic but expired, or an unsupported version | `409 cursor_expired`; start again from page one |

## Caching

An **output cache** keeps whole responses in each instance's memory and replays them without a database query.

| Response | Server cache | `Cache-Control` header |
|---|---|---|
| First list page | Up to 15 s, never past midnight UTC | `public, max-age=` same seconds |
| Page with a `cursor` | Not cached | `no-store` |
| Job details | 1 hour | `public, max-age=86400, immutable` |
| Errors, including `404` | Not cached | `no-store` |

- Each combination of parameters is cached separately.
- Cursor pages bypass the cache, so every request checks the token.
- The midnight cap keeps closed jobs out of cached pages.
- A new job can take up to 15 seconds to reach the first page.

## Errors

Errors are problem details JSON (`application/problem+json`) with a `traceId`. Endpoint errors also carry a `code`.

| Status | `code` | Cause |
|---|---|---|
| 400 | `invalid_query` | Bad parameter |
| 400 | `invalid_id` | ID is not a non-empty UUID |
| 400 | `invalid_cursor` | Cursor failed its checks |
| 404 | `job_not_found` | No such job |
| 409 | `cursor_expired` | Cursor expired |
| 503 | `search_unavailable` | Database unreachable or timed out |
| 500 | — | Unexpected failure |

Responses never expose stack traces, query values, cursors or secrets.

## Signing keys

Every instance needs the same key ring, active key ID and lifetime, set outside the repository. Each key is base64 of at least 32 random bytes. The ring holds 1 to 4 keys.

```powershell
$env:Cursor__Keys__current = [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$env:Cursor__ActiveKeyId = 'current'
$env:Cursor__LifetimeMinutes = '15'
```

Without a key, the Development environment uses a public example key. Other environments refuse to start without a valid key, and reject the example key.

**Rotation:**

1. Add the new key to every instance, keeping the old key active.
2. Make the new key active everywhere.
3. Wait until old tokens expire (up to 60 minutes), then remove the old key. Leftover old tokens then return `400 invalid_cursor`.

Never change the value behind an existing key ID.

See also: [Docker and local development](docker.md) and [read performance](performance/read-performance.md).
