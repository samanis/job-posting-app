# Stage 7: Browser and query-performance verification

Read apps/job-search/prompts/shared-requirements.md. Stages 1-6 must be complete. Implement only this stage.

1. Add Playwright desktop Chromium and mobile Chromium projects, managed isolated dev-server port, failure traces/screenshots and ignored output. Include separate strict E2E type checking and install/run/report scripts. Keep backend fixtures in e2e and label them as intercepted mocks.
2. Cover initial listing, empty response, filters/Enter/Clear, debounce request count, rapid query change with delayed responses, sort/cursor reset, pagination, Back/Forward, detail and direct deep link, safe descriptions, unavailable detail, refresh and cached revisit, 202/malformed success, query 4xx, 429 delay, 5xx/network recovery. Mock server data changes to demonstrate refresh can discover a new job; explicitly avoid claiming posting/search integration.
3. Assert bounded DOM and payload behavior with a large conceptual dataset served one page at a time. Assert list fetch does not include full descriptions or trigger per-card detail calls. Requests must scale with user actions, not total job count. Use clock/request gates instead of arbitrary sleep.
4. Capture and inspect desktop/mobile screenshots for results, full details, empty and error states. Test keyboard-only search/navigation and viewport overflow. Add automated accessibility checks if useful, while documenting their limits.
5. Record reproducible production-bundle sizes and configured budgets. Inspect network counts and a representative browser performance trace for avoidable rendering/request work. Separate mocked browser overhead from API latency; do not assert universal timing thresholds or invent high-load throughput. Server scalability needs later real API/database/load testing.

Acceptance: E2E type check, browser suite, unit coverage and production build pass. Document measured sizes/request counts, visual findings and remaining browser/backend coverage. Fix reproducible issues before claiming completion.
