# Checkbox selection investigation

User contract: check multiple messages with ordinary checkbox clicks, preview other rows without losing checks, select/clear loaded messages from an aligned header checkbox. Shift is optional, never required.

## Reproduction and cause

On Windows/.NET 10 WPF, the pre-fix executable was built to `artifacts/selection-red` with `dotnet build tools/ServiceBusEmulatorExplorer.InvestigationPrototype --no-restore -o artifacts/selection-red`. Running that executable with `--verify-selection artifacts/selection-red-proof` exited 1:

> Previewing another row cleared existing checkbox selections. Header checkbox center differs from row checkbox center by 4.5 pixels.

Ranked hypotheses checked against the failing path:

1. Native row selection owns checkbox state. Confirmed: `Messages_Selected` cleared flags from `RemovedItems`, and each checkbox rebuilt `SelectedItems`. A normal preview selection reproduced the loss without refresh or timer activity.
2. Refresh replaces checked messages. Not the cause of this reproduction: it occurs before any refresh. Existing refresh retention checks were retained.
3. Bulk selection causes repeated UI updates. Confirmed structurally: each flag raised a global workspace change and the grid cleared/re-added selected rows. The regression bounds bulk notifications to at most four.
4. Header padding causes alignment error. Confirmed by rendered center coordinates (4.5 pixels before the fix).

The fix keeps WPF single-row selection for preview only, stores checks independently, batches header changes, and lets focused checkboxes own Space activation. Header content is centered in the same 45-pixel column. Connection changes suppress transient native-selection events, so disconnect cannot restore a stale preview.

## Verification scope

The focused executable proof passed after the change. It exercises actual WPF checkbox activation and routed keyboard handling, verifies partial/all/none transitions, and measures rendered alignment. It does not synthesize physical mouse input. The full [walkthrough](verification.md) repeats these checks at desktop and compact sizes, plus refresh, paging, replay targets, and disconnect/reconnect.

The same revision adds an editable JSON view using pinned AvalonEdit 6.3.1.120. JSON is indented only on opening; token colors are shared with the inspector. Rendering does not rewrite editor content or undo history. The walkthrough covers rendered colors, middle-value edits, caret position, undo/redo, incomplete JSON, compact long-string wrapping, cancel, and replay of exact edited text.

This remains a synthetic-data prototype; no broker operations are involved.
