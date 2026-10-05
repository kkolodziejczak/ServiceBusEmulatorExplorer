# Workbench search suggestions and inspector tabs

Approved 2026-10-02: [review image](design/approved/19-workbench-search-and-inspector-tabs.png).

- Reuse the Investigation suggestion presentation in Workbench namespace search: entity icons, names and type labels. Choosing an entity filters the namespace/library context; it must not browse broker messages or retarget the open template destination.
- Template search offers grouped template and folder results. Template rows include the containing folder path. Choosing a template opens and reveals it through ordinary template selection; choosing a folder reveals and selects it without replacing the open draft.
- Retain combined namespace and template filtering. Search suggestions respect that context. Clear restores the corresponding filter, not the template destination. No message-ID or correlation-ID search actions belong in these Workbench popups.
- Support pointer selection, Up/Down, Enter and Escape. Close on clear, outside interaction and workspace changes. Bound popup height and width; trim long names and expose full context accessibly. An unmatched query must not leave stale suggestions.
- Wide Compose keeps Body visible with adjacent Properties and Variables tabs above the inspector. Both labels remain visible; the active tab uses the shared selected-tab treatment. Selecting an already active tab keeps that view selected.
- Medium/small layouts retain Body / Properties / Variables tabs. Resizing preserves the draft, selection, active inspector and last editing context under the existing responsive contract.

The generated image is a layout reference. Existing production controls, field values and typography remain authoritative where image generation introduces incidental differences.
## Verification

- PASS: 426 App tests; final 20-test focused rerun includes namespace and folder routed pointer selection, keyboard navigation and Escape, dirty-switch Cancel, combined filtering and draft/destination retention.
- PASS: 16 fresh real-WPF captures at 1700, 1695, 1500, 1200 and 980 DIPs; grouped search popups, both wide inspector tabs, resize state retention and Prepare/back. Popup pixels are captured from the real popup visual and composed at its screen-relative position.
- PASS: independent code and visual review, clean build and convention/path checks. Local evidence: artifacts/search-tabs-proof, focused-final.log, full.log and capture-3.log.
- Not verified: physical desktop input, screen-reader output and multi-monitor/DPI behavior. Routed WPF events do not establish those proofs. Sending remains simulated and the library remains session-only.