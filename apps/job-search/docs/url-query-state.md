# Job Search app: filters and paging in the URL

The URL holds the whole search: filters, sort, page size and current page. So users can share or bookmark a link, and Back and Forward work. The app loads results from the URL. The search controls only change the URL.

## URL parameters

| Parameter | Meaning | Rules |
|---|---|---|
| `q` | Keywords in the title or description | Up to 200 characters |
| `department`, `location` | Text the field must contain | Up to 100 characters each |
| `sort` | `newest` or `closing-soon` | Default `newest` |
| `limit` | Jobs per page | 1 to 50, default 20; the menu offers 10, 20 and 50 |
| `cursor` | Page token from the API | Up to 2048 characters |

Defaults are left out of the URL, and unknown parameters are ignored. An invalid value shows a message and falls back to its default. The page token is then dropped, because it only fits the exact search it came from.

## How changes reach the URL

| Action | URL changes | Browser history |
|---|---|---|
| Typing in a text filter | 300 ms after the last keystroke | Replaces the current entry |
| Enter, Search, sort or page size | At once | Adds an entry |
| Next, Previous, First page | At once | Adds an entry |
| Clear filters | Goes to plain `/jobs` | Adds an entry |

Replacing the entry while typing keeps Back from stepping through every keystroke. With languages that compose characters, such as Japanese, the app waits until the character is finished. A changed search always starts at the first page.

## Paging

Next puts the API's page token in the URL. The API returns no total count, so there are no page numbers. For Previous, the app remembers up to 50 visited tokens for the current search. That memory survives visits to job details but not a reload or a new search. So a shared link to a later page has no Previous, but First page always works. An expired token shows "Reload first page".

## Back, Forward and shared links

On Back or Forward, the app re-reads the URL, restores the controls and shows the matching results. Recent results come from the browser cache.

Each job link carries the current search. "Back to jobs" returns to `/jobs` with that search. Only known parameters are copied, and the app never takes a return address from the URL. So a crafted link cannot send users to another site. Job links are Angular `UrlTree` objects (pre-parsed URLs), which keeps the search parameters intact on click.

## Outdated requests

When the URL changes, the app cancels the request in progress. A slow, outdated response can never replace newer results. See [list-and-detail-experience.md](list-and-detail-experience.md).
