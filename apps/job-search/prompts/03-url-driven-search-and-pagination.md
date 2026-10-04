# Stage 3: URL-driven query controls

Read apps/job-search/prompts/shared-requirements.md. Stages 1-2 must be complete. Implement only this stage.

1. Build accessible keyword, department and location text filters plus allowlisted sort. Do not add currency, application or posting controls. Use stable Angular 22 form APIs appropriate to these optional filters.
2. Specify URL parsing/serialization: trim text, impose documented length bounds (proposed q 200 and filter 100 characters), allowlist sort/limit, validate bounded opaque cursor without decoding its internals, and ignore unknown parameters. Invalid URL state must show useful feedback or a documented canonical fallback, never dispatch unbounded input. Do not lowercase text unless server matching semantics explicitly permit it.
3. Maintain input drafts separately from committed URL query state. Debounce keyword/filter changes 300 ms, normalize and distinct the committed query; Enter submits immediately and Clear commits once. IME composition must not trigger partial queries. Filter/sort/limit changes reset cursor and page history.
4. Use URL committed state as the single query trigger. Debounced edits replace history; explicit pagination/navigation create useful history entries. Back/forward restores controls and results without echo updates or duplicate requests.
5. Implement bounded cursor pagination with Next and a defined Previous strategy: maintain at most 50 cursor-history entries per query, treat arbitrary deep-linked cursors as having unknown prior pages, disable Previous when none is known, and offer first page. Avoid invented page counts. Guard repeated/invalid next cursors.
6. Preserve list query URL when opening details and return to it. Keep all user text encoded and safe.

Acceptance: deterministic tests for parser round trips, bounds, debounce/Enter/Clear/IME, distinct values, cursor reset, previous/next/deep links and back/forward. Assert exact request counts when wiring begins; no double trigger from URL plus form. Build passes.
