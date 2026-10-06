# Job list and detail experience

The client now renders available-job summaries and full authoritative details through the proposed search API. No backend or production mock dataset is included; live use requires a compatible API. Eventual visibility of jobs from the posting app remains an unverified server integration obligation.

Listings are bounded by validated server pages (maximum 50). Cards use stable id tracking, semantic heading links, department/location, decimal salary range without inferred currency and calendar closing dates. Presentation/date/link values are computed when data or criteria changes; there is no client dataset filtering/sorting or per-card detail request.

Loading reserves space. Ready/empty announcements use a persistent polite live region; draft keystrokes are not announced. Refresh/stale warnings distinguish previously loaded data from current confirmation. Errors show text plus color and explicit recovery controls. Expired cursor recovery offers first-page reload; other retries preserve committed criteria and the throttle policy. No automatic retries.

Lazy detail routes cancel old route-id reads and use the shared cache/coordinator. Full details show title, department, location, salary minimum/maximum, closing date, posted UTC timestamp, identifier and multiline plain-text description. Calendar dates are formatted at local noon without UTC conversion; timestamps explicitly use UTC. Unavailable 404/410 clears old detail data and removes Retry. Unsupported success, network/service errors and not-ready responses show safe messages with manual retry. A failed refresh may retain labeled stale details except when the server confirms unavailable.

Navigation focuses the detail h1, including reused route-id transitions. The title uses the loaded job title; loading/unavailable falls back to Job details. Background refresh does not move focus. Card activation remembers one listing identity/id; returning to the same query restores that card's focus when it is rendered from cache, otherwise the list h1. Focus provides native browser scroll-to-element behavior; exact prior pixel scroll restoration is not implemented. Direct detail links return to /jobs. Return criteria are allowlisted URL parameters, never external return URLs.

Verification: component/router/HTTP tests cover rendered states, data identity, date-only formatting, plain-text safety, stable card DOM, no per-card requests, detail cancellation, focus and return navigation. Limited real Chromium visual review captured desktop 1280x960 and mobile 393x851 list/detail/empty/error states using intercepted fixtures, including long unbroken text and long descriptions. All eight cases had no horizontal overflow; screenshots were inspected. Temporary review artifacts live in ignored tmp/visual. This is not the full stage 7 Playwright suite or a complete accessibility audit; real backend behavior and other browsers remain unverified.

References: [Angular afterNextRender](https://angular.dev/api/core/afterNextRender), [Angular DatePipe](https://angular.dev/api/common/DatePipe).

Stage 7 added the full 36-check desktop/mobile Chromium suite and inspected fresh results/details/empty/error screenshots. Query-bearing listing links bind UrlTree objects so filters/cursors remain query parameters during actual activation.
