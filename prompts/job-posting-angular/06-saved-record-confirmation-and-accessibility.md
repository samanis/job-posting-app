# Stage 6: Confirmation and accessible user experience

Read `prompts/job-posting-angular/shared-requirements.md` first. Stages 1–5 must exist; implement only this stage.

## Tasks

1. Build the confirmation component using signal inputs where applicable. Show all saved fields, job identifier, and saved timestamp from the API record. Preserve multiline description as plain text.
2. Format salary numbers without inventing a currency. Format closing dates without shifting the calendar day through UTC parsing. Clearly label the saved timestamp.
3. Implement an explicit “Post another job” action that starts a clean logical attempt only after confirmed success. Ensure navigation/refresh behavior is consistent with the existing attempt lifecycle.
4. Complete responsive styling for form, summary, submitting, server errors, pending outcomes, throttling, and confirmation. Maintain keyboard navigation, visible focus, appropriate status/alert announcements, and sufficient contrast.
5. Move focus deliberately after success and invalid submission. Do not announce every keystroke as an alert or expose raw diagnostic payloads.

## Acceptance and tests

- Confirmation displays the actual API record even when it differs from the request.
- Assert plain-text rendering of HTML-like descriptions, date formatting, number formatting, and start-another-job behavior.
- Test live region content, focus changes, labels, keyboard-operable actions, and all disabled states.
- Review the app visually at narrow and wide widths using available tooling; report any check that could not run.
- Run tests and build. Do not add a search feature or job editing/deleting.
