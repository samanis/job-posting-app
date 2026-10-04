# URL-driven filters and cursor navigation

Committed URL query criteria are the query trigger. Text-input drafts use signals and native controls with explicit event handling: no duplicate form/resource query subscription. Stage 4 connects reads and supplies nextCursor from validated pages; stage 5 owns listing/detail rendering.

Recognized URL parameters: q, department, location, sort, limit, cursor. Trim text without changing case; q is at most 200 characters, department/location 100 each. Sort is newest or closing-soon. Limit is an integer 1-50, defaults to 20 and is serialized only when non-default. Cursor is opaque, at most 2048 characters and contains no whitespace/control characters. Repeated recognized parameters are invalid. Unknown parameters are ignored, never forwarded to reads or detail-return links.

Invalid recognized parameters produce visible feedback with safe defaults: invalid text becomes empty, sort newest, limit 20, cursor null. Any repaired criteria discards the cursor. The URL is not automatically rewritten, avoiding history churn; corrected user actions serialize canonical parameters. Clear always navigates to /jobs unless already there. Invalid oversized drafts display feedback and do not commit until corrected.

Keyword/department/location edits debounce 300 ms, replacing the current history entry. Submit/Enter and select changes commit immediately; explicit changes, Clear and pagination create history entries. Only differing normalized criteria navigate. Submitting unchanged criteria retains the current cursor; changed filters/sort/limit start at the first page. IME composition suspends commit across all composing controls; final text is debounced once. Navigation and destruction cancel pending debounce. Incoming route state restores controls without echo navigation.

Cursor history is session memory in a root service, one criteria set and at most 50 visited cursors. It survives list/detail navigation but not refresh or criteria changes. Unknown deep links have no Previous; First page remains available. Next rejects malformed cursors and loops to the current/prior page; a previously visited forward page is allowed. Evicted history cannot be reconstructed and has no Previous. No total/page count is invented.

Detail links carry canonical criteria on /jobs/:id, and detail return links always target /jobs with those recognized parameters. There is no external returnTo URL; untrusted returnTo parameters are ignored. Stage 5 renders full details and uses detailUrl for listing links.

Router tests verify state restoration for URL transitions; actual browser Back/Forward, IME and layout checks are deferred to Playwright in stage 7. Stage 4 HTTP tests verify dispatch counts and cancellation against controlled responses, not backend performance.

References: [Angular route state](https://angular.dev/guide/routing/read-route-state). Signal Forms were considered; optional filters use native inputs with signal drafts to make URL ownership and composition timing explicit.
