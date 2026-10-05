# Scheduling controls

Approved 2026-10-02: [review image](design/approved/20-scheduling-controls.png).

- Use a flat date field and a calendar popup styled with the shared app colors, borders, typography, selected-day and keyboard-focus states. Keep month navigation and day selection accessible.
- Replace free-text time with two noneditable selectors: hours 00–23 and minutes 00–59. Default to 14:30; retain tomorrow as the initial date.
- Align the radio glyph and label vertically for Send now / Schedule and UTC / Local.
- Keep controls compact and inside the Target card at supported window widths. The date field sizes to the displayed date, compact horizontal padding and the calendar button, with left-aligned text centered vertically inside its 32-DIP frame. Clicking anywhere on the field opens the calendar after the pointer is released inside the field, so the opening click cannot dismiss the popup; dates can only be chosen from the calendar, with manual typing and paste disabled. Enter, Space, F4 and Alt+Down open it from keyboard focus; calendar navigation, selection and dismissal remain available. This 2026-10-03 refinement supersedes the earlier time-row width rule. Popup examples on the design board illustrate states, not additional panels.
- Preserve UTC/Local resolution, rejection of invalid or ambiguous local times, future-time validation, and one scheduled instant for the batch. Revalidate before dispatch.
- Keep sending simulated in this prototype; this change does not alter broker operations.

Existing shared component tokens are authoritative where the generated image introduces incidental differences.

## Verification

- PASS (2026-10-03): 16 focused WPF tests cover content-sized date geometry at 980/1100/1500 widths, read-only date text and UI Automation values, pointer/keyboard calendar opening, accessible calendar children, selection and dismissal, disabled state, time selectors, UTC/local resolution and scheduling validation.
- PASS: the real WPF destination flow captures scheduling and calendar/hour/minute popup states at 980, 1100 and 1500 pixels, including selected-date and resolved-time updates. Local evidence: artifacts/scheduling-controls-proof.
- PASS: clean builds, convention/path checks and independent bounded source review.
- Limitations: calendar interaction proof uses routed WPF mouse events; selector proof uses UI Automation providers. That earlier proof did not verify physical input, screen-reader output or multi-monitor/DPI behavior; see the subsequent click-regression proof below. Sends remain simulated.
- Historical baseline (2026-10-02): full App regression suite, 430 tests (92 seconds). This full suite was not rerun for the calendar-only refinement.

### Calendar click regression (2026-10-03)

The complete pointer gesture previously opened on press, then the popup treated the release over the field as an outside click and immediately closed. The date field now captures the press and opens only after release inside its bounds. Capture loss or release outside must not open it. The native keyboard and UI Automation paths remain available.

The opt-in `CalendarPointerInteractionTests` uses guarded native button input over its own isolated window without moving the pointer; it requires an interactive Windows desktop and `SBE_RUN_POINTER_TESTS=true`. Synthetic preview-down alone is not evidence for this regression. The initial physical reproduction recorded one calendar-open event on press and one close event on release, with no date change; the same two text/icon cases passed after the fix. Sandbox-native-message attempts did not reach the control and are excluded from the proof.

Verification: 10 native-input cases pass, covering text/icon opening at 760/980/1100/1500 window widths, Escape dismissal and reopening, capture loss, and release outside the field. Sixteen scheduling tests pass for date selection, month navigation, keyboard opening, read-only automation, disabled state, layout and scheduling validation. All eight real WPF popup captures were inspected; text/icon captures match at each width. Screen-reader output and a comprehensive multi-monitor/DPI matrix remain unverified. Evidence: `artifacts/calendar-click/physical-red.log`, `physical-green.log`, `settled-pointer-matrix.log`, `scheduling-regression.log`, and `rendered/` (local ignored artifacts).
