# Investigation UI language and final audit

Status: prototype approved as complete by the user on 2026-09-12, baseline `126c824`; rendered flows and supported window sizes verified. Preserve this UI during production implementation. The production application has not been promoted.

Authority: the latest decisions in the “Audit and simplify Service Bus UI” conversation, then the current [prototype preview](../tools/ServiceBusEmulatorExplorer.InvestigationPrototype/preview.png). Earlier generated images are historical references. Preserve the approved compact connection toolbar, three panes, full-width console, and bottom time selector.

## Audit findings and current implementation

The findings below describe the original audit baseline. The prototype now has a [shared style dictionary](../tools/ServiceBusEmulatorExplorer.InvestigationPrototype/SharedStyles.xaml), shared action templates, explicit button focus rings and normalized Settings profile/tab selection. Verify the complete affected surfaces before closing findings; code changes alone do not constitute a visual pass.

| Priority | Finding and evidence | Required correction |
| --- | --- | --- |
| P1 | White 14 DIPs labels on primary blue `#0078F8` have about 4.16:1 contrast; Settings `#087CF0` about 4.08:1. Hover opacity further reduces contrast. | Use one primary palette; proposed existing `#0069FA` provides about 4.77:1 with white. Verify hover/pressed too; do not fade the whole button. |
| P2 | [Notification](../tools/ServiceBusEmulatorExplorer.InvestigationPrototype/WatchNotificationWindow.cs) uses native action-button templates and different default typography. | Share main-window typography, action styles and states; keep compact notification layout and rounded outer panel. |
| P2 | [Settings](../tools/ServiceBusEmulatorExplorer.InvestigationPrototype/PrototypeSettingsWindow.xaml) has blue profile cards but gray native Connections selection; main tabs and Settings tabs have different selected fills. | Use one selection palette and explicit templates for tabs/lists across windows. |
| P2 | Focus borders differ between controls; a color-only border trigger on a borderless icon button may be invisible. | Give every interactive control a visible, unclipped focus ring; verify keyboard navigation separately from logical focus. |
| P3 | Settings, Pause, Find related, Replay and Discard still use font glyphs. Watch/Refresh/Copy now use paths. | Replace remaining action glyphs with named vector assets in the same family, preserving recognizable meanings. |
| P3 | Inspector title is 24 in XAML but 25 in code; Settings 22; notification summary 15 (its application title is 12). | Apply the role-based sizes below instead of one-off overrides. |
| Tradeoff | At minimum size with the log expanded, only one full message row may be visible. | Keep the existing responsive layout for first promotion; explicitly test this tradeoff. Do not silently claim ample space at minimum size. |

Primary source: [main XAML](../tools/ServiceBusEmulatorExplorer.InvestigationPrototype/PrototypeWindow.xaml), [layout code](../tools/ServiceBusEmulatorExplorer.InvestigationPrototype/PrototypeWindow.xaml.cs), Settings and notification sources above. Rendered desktop, compact, Settings, Watch, notification and refresh-selector states were inspected from the latest `artifacts/toolbar-icons-proof` output. That output is local/ignored; the committed preview is the durable desktop reference.

## Shared tokens

Sizes below are WPF device-independent units (DIPs), not physical pixels. Use named WPF resources in the real app, shared by the shell, Settings, dialogs and notifications. Values below are the approved defaults; deviations must represent a named density or semantic variant.

| Role | Value / rule |
| --- | --- |
| Font family | Segoe UI; Consolas for JSON and console |
| Type scale | 14 DIPs controls/body; 12 DIPs metadata; 11 DIPs badges/timestamps only; 16 DIPs section title; 24 DIPs inspector title; 18 DIPs compact title; native Settings window title only; 16 DIPs notification summary |
| Code/console | JSON 14 DIPs; console 12 DIPs; always wrap body text; preserve raw content |
| Primary text / secondary / action text | `#17213D` / `#627692` / `#234266` |
| Canvas / raised / toolbar | `#FAFCFF` / `#FFFFFF` / `#F5F9FE` |
| Dark editor/log | `#1C2937`, light text; retain semantic syntax colors |
| Primary / hover / pressed | Blue baseline `#0069FA` / `#005BD8` / `#004FBD`; active profile supplies accessible variants with white labels; verify every palette/state |
| Neutral hover / selected | `#EAF4FF` / `#DBEDFF` |
| Control border / divider | `#CBD8E8` / `#DFE6EE` |
| DLQ / modified | Orange and amber backgrounds with dark text; always accompany color with a label |
| Entity colors | Blue for queues/topics; purple for subscriptions; preserve distinct geometry |
| Profile identity | User-selected blue, purple, teal, orange or red theme across buttons, selection and surface tints, dropdown swatch and top accent border; profile name remains visible. Apply the active profile consistently to Settings and related windows; editing an inactive profile does not preview its theme. Semantic health, DLQ and red Delete colors remain fixed. |
| Connection health | Labeled green Connected, red Disconnected or amber Warning indicator, separate from profile accent; warning tooltip describes the reason |
| Spacing | Prefer 4, 8, 12, 16, 24; use existing 7 DIPs icon-label gap consistently |
| Corners | 3 DIPs controls; 4 DIPs menu/hover surfaces; 9 DIPs notification outer panel |
| Icons | 16 DIPs optical box, 1.5 DIPs strokes; chosen rounded Copy retains 1.4 DIPs stroke and 16×18 aspect. Chevrons 8×5. No Unicode substitute glyphs for actions. |
| Buttons | Standard minimum 32 DIPs height; compact footer selector 24 DIPs; icon-only target at least 28×28 where space permits; content centered vertically |
| Density | Toolbar 40 DIPs; message rows 54 DIPs desktop / 50 DIPs compact; preserve splitter resizing |
| Viewports | Desktop 1500×1000; compact 1100×800; minimum window 980×640. Check 100%, 125%, 150%, 200% Windows scaling and multi-monitor movement. |

Do not confuse glyph dimensions with hit targets. Keep Copy beside the correlation text. Preserve the crossed-out/filled Watch distinction and the selected rounded-copy shape. The app name/icon belongs in the native title bar, not a duplicate banner.

## State contract and visual gate

- Buttons, tabs, checkboxes, selectors and menu items: normal, hover, keyboard focus, pressed, selected/checked and disabled must be explicit and consistent. Avoid opacity-only hover treatment.
- Disable an action while it cannot safely run, including duplicate submission during an operation; show progress and keep unrelated actions usable. Long operations need cancellation where safe.
- Empty, loading, stopped, unavailable counts, partial failure, long IDs and malformed/non-JSON bodies must remain distinguishable.
- Test contrast of normal text at 4.5:1 and meaningful control/focus boundaries at 3:1; include selected rows and dark surfaces.
- Preserve the accessible names and routed behavior already covered by proofs. Verify physical keyboard traversal, Escape, Space, Enter, screen-reader labels and focus restoration; these are not proven by logical-focus assertions alone.
- Capture the complete affected surface after each style migration. Include expanded/collapsed log, Watch on/off, notification hover, profile selection, invalid/empty search, dirty editor and compact sizing.
- Verify global and topic Watch scope, Active/DLQ independence, future-entity inheritance and child overrides. The searchable inclusion tree uses checked/unchecked/mixed states; a branch choice applies to its descendants and a later specific child choice overrides inherited inclusion. A scope summary must not imply a topic itself is a receiving endpoint.
- Delete remains a visible action with exact typed `DELETE` confirmation showing targeted Active/DLQ counts. Checked messages form the batch; otherwise the focused message is targeted. Cancellation changes nothing; unseen messages are never implied by select-all.

- General settings omit the duplicate connection picker. Automatically connect when switching profiles is an opt-in saved switch; it does not bypass warnings or connect on profile edits.
- Add connection opens a uniquely named empty editor without changing the active connection; Save persists its color and optional warning. Saved warnings gate switching, Connect and restored startup attempts. Cancel preserves the previous connection or leaves the pending attempt disconnected.
- Watch search uses an inline magnifier and concise placeholder; keep its accessible name without a verbose duplicate instruction.

Current gate: **visual consistency and routed flows PASS** at the three supported window sizes, with 347 walkthrough checks and 11 real tray checks. Typed confirmation, inclusion-tree Watch, profile colors/health and protected preference save/reopen are covered by the [walkthrough](../tools/ServiceBusEmulatorExplorer.InvestigationPrototype/verification.md). Full-profile themes, saved warning flows, Add connection and simplified Watch search pass the current rerun, including saving while a warning is pending and restored DLQ browsing. **Broader physical keyboard, screen-reader and Windows DPI/multi-monitor evidence remains BLOCKED (not collected).** Synthetic interactions do not establish broker correctness or a complete accessibility audit.

## Final chrome polish (2026-09-12)

The profile accent forms a continuous 3-DIP divider below the connection toolbar, meeting the pane splitters. Settings uses a conventional toothed cog and the native window title without a duplicate content heading. Each masked connection field has the shared rounded Copy icon, an accessible name and a tooltip; copying uses the current field value without logging it. Empty fields disable Copy.

All shared scrollbars use an 18-DIP hit lane, a rounded 8-DIP visible thumb and profile-themed hover/drag feedback. Preserve wheel, keyboard, page and thumb behavior in both orientations. Dark editor/log backgrounds remain dark beneath the transparent track.

Chrome-polish verification: zero-warning build and 347 routed checks pass. Actual desktop/compact/minimum screenshots, masked-field copying, Watch scrolling, and page/drag commands in both orientations were checked. Current rendered evidence is reflected in the linked previews and walkthrough; prior physical-input/DPI limitations remain. One initial run encountered external clipboard contention while restoring clipboard contents; the complete final rerun passed.
