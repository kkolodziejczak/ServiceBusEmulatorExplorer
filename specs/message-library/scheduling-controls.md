# Scheduling controls

Approved 2026-10-02: [review image](design/approved/20-scheduling-controls.png).

- Use a flat date field and a calendar popup styled with the shared app colors, borders, typography, selected-day and keyboard-focus states. Keep month navigation and day selection accessible.
- Replace free-text time with two noneditable selectors: hours 00–23 and minutes 00–59. Default to 14:30; retain tomorrow as the initial date.
- Align the radio glyph and label vertically for Send now / Schedule and UTC / Local.
- Keep controls compact and inside the Target card at supported window widths. The date field matches the combined hour/minute row width (or shrinks to its available space), with left-aligned date text centered vertically inside its 32-DIP frame; refinement approved 2026-10-03. Popup examples on the design board illustrate states, not additional panels.
- Preserve UTC/Local resolution, rejection of invalid or ambiguous local times, future-time validation, and one scheduled instant for the batch. Revalidate before dispatch.
- Keep sending simulated in this prototype; this change does not alter broker operations.

Existing shared component tokens are authoritative where the generated image introduces incidental differences.

## Verification

- PASS: 12 focused WPF tests cover selector ranges, defaults, keyboard focus, radio alignment, UTC/local resolution, past dates, invalid/ambiguous local times, and calendar navigation, selection and dismissal.
- PASS: the real WPF destination flow captures scheduling and calendar/hour/minute popup states at 980, 1100 and 1500 pixels, including selected-date and resolved-time updates. Local evidence: artifacts/scheduling-controls-proof.
- PASS: clean builds, convention/path checks and independent bounded source review.
- Limitations: calendar interaction proof uses routed WPF mouse events; selector proof uses UI Automation providers. Physical input, screen-reader output and multi-monitor/DPI behavior were not verified. Sends remain simulated.
- PASS: full App regression suite, 430 tests (92 seconds).
