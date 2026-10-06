# Job Search app: list and detail pages

The Job Search app lets job seekers browse open jobs at `/jobs` and read one job at `/jobs/:id`. Filters, sort and paging live in the URL; see [url-query-state.md](url-query-state.md).

## What the user sees

**List.** Each job is a card with its title (a link to the details), department, location, salary range and closing date. Salaries have no currency symbol. Dates are shown as calendar dates, with no time-zone shift. Descriptions are left out to keep pages small.

**Details.** The page shows every field, plus the posting time in UTC, the job ID and the full description. The description is plain text with its line breaks kept. "Back to jobs" returns to the list with the same filters and page.

## Loading, empty and error states

| State | What the user sees |
|---|---|
| Loading | A progress bar and "Loading jobs…" |
| No results | "No jobs match these filters", which is distinct from an error |
| Error or timeout (10 seconds) | A plain message and a Retry button |
| Results page expired | A "Reload first page" button |
| Too many requests (`429`) | Retry stays blocked until the `Retry-After` time |
| Job not found (`404` or `410`) | "This job is no longer available", with no Retry button |

The app never retries on its own.

## Browser cache

The app keeps up to 50 recent list pages and job details in memory for 30 seconds. A revisit within that time shows the page at once, with no request. After that, the saved copy stays visible, clearly labelled, while a fresh copy loads. If loading fails, the old copy stays, marked as possibly out of date. "Refresh" buttons always fetch fresh data. Reloading the page clears the cache.

## Cancelling outdated requests

When the filters or the job change, the app cancels the request in progress. It uses RxJS `switchMap`, which switches to the newest request and drops the old one. So a slow, outdated response can never replace newer results.

## Accessibility and focus

- A polite live region announces the result count without interrupting the user. Typing is not announced.
- Opening a job moves focus to its title.
- Returning to the list focuses the card the user opened, if it is on screen. Otherwise focus goes to the list heading.
- Long text wraps, so pages never scroll sideways.

Playwright tests check these behaviours in desktop Chrome and an emulated Pixel 7 phone, using fake API responses.
