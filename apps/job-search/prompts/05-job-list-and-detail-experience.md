# Stage 5: Listings, full details and accessibility

Read apps/job-search/prompts/shared-requirements.md. Stages 1-4 must be complete. Implement only this stage.

1. Render a bounded list of summary cards with semantic heading links, department, location, salary range and closing date. Use @for track job.id and computed presentation state; do not perform filtering/sorting or expensive work in template expressions. Do not request descriptions/detail records for each card.
2. Connect list state to accessible initial loading, refreshing/stale, empty, query-error, rate-limit and retry controls. Reserve loading space to reduce layout movement. Empty results must be distinguishable from failures. Announce settled result changes politely, not every keystroke.
3. Implement lazy full-details page driven by route id, using the coordinator and validated authoritative detail record. Show all job fields, plain multiline description, correct date-only formatting and explicitly labeled timestamp timezone. No inferred currency or unrequested apply action.
4. Handle detail loading, unavailable 404/410, unsupported success, temporary failure and retry. Preserve a safe internal return URL; external/untrusted return targets are prohibited. Direct links get a useful default back-to-list link.
5. Provide route titles, focus on detail heading, keyboard-accessible controls and predictable return focus/scroll where feasible. Avoid stealing focus on background refresh or each search update. Errors must use text as well as color.
6. Review narrow and desktop layouts, long unbroken titles/locations, long descriptions, empty results and errors. Reuse visual conventions, not source imports, from posting.

Acceptance: component/router tests prove all rendered states, plain-text safety, no per-card detail requests, detail id changes cancel prior reads, deep-link/back behavior, keyboard focus and date rendering. Relevant tests/build pass. No mocked cross-app visibility presented as real integration.
