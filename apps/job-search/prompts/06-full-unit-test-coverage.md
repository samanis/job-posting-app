# Stage 6: Complete unit coverage

Read apps/job-search/prompts/shared-requirements.md. Stages 1-5 must be complete. Implement only this stage.

1. Audit actual coverage across every authored executable src TypeScript file, including bootstrap, configuration and files not imported by tests. Enforce 100% per-file statements, branches, functions and lines using the supported Angular/Vitest configuration.
2. Close meaningful behavior gaps: runtime schema boundaries, all HTTP outcomes, URL canonicalization, debounce/IME, history, cursor guards, exact cache TTL/LRU, simultaneous consumers, cancellation and teardown, refresh failures, throttling and all list/detail UI states.
3. Assert requests and outcomes rather than private implementation details. Use injected clocks and deterministic scheduling. Keep all unit specs under tests. Do not suppress uncovered code, alter production behavior merely to satisfy coverage, or reduce thresholds.
4. Verify the gate detects an unimported temporary production file and fails on missing coverage. Remove the probe and rerun the clean gate.
5. Document inclusion/exclusion globs, commands, metrics and limitations in docs/testing-and-coverage.md. Coverage does not establish CSS quality, browser accessibility, backend correctness or server scale.

Acceptance: coverage command passes with all four metrics 100% per authored executable file; probe demonstrated gate failure; strict build passes. Report actual totals and commands, not estimated coverage.
