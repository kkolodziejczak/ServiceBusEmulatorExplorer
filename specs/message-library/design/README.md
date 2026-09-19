# Message Library — approved visual contract

The user approved **only the five boards below** and explicitly required **pixel-perfect implementation**. They supersede the seven-board pack, compact mockups, filtering collage and alternate layouts. Superseded repository mockups were deleted at the user's request; only these five boards and the flow diagrams remain. No compact mockup is approved.

Read with the [main plan](../../message-library-implementation-plan.md), [topic context contract](../topic-context.md), [properties/cancellation](../message-properties-and-cancellation.md), and existing [app UI language](../../ui-language.md).

## Mandatory pixel-perfect acceptance

**Implement the approved UI pixel-perfect. “Similar”, “inspired by” or only functionally equivalent is not acceptable.** Match pane order/proportions, spacing, alignment, typography, density, colors, borders, icon style, action placement and states. Do not redesign, replace the library with a message-list tab, remove the namespace sidebar or add a permanent live-message pane.

Only the explicit image corrections below, real runtime data and existing production chrome/vector resources are exceptions. User-approved images govern composition; shared WPF tokens govern exact font/icon/brush rendering. Surface any remaining conflict before implementing the conflicting part.

Use actual production WPF resources and routed controls. Capture deterministic fixtures at 100% scaling; compare side-by-side and with overlay/difference images. Board A's native extent is 1642×958; use a matching client-area crop, recording native frame exclusions. B–E are collages: compare corresponding panel/dialog crops, not entire boards as one window. Never stretch a screenshot to hide geometry differences.

Flat-color fills and component bounds must match at the reference scale. Font antialiasing/native frame differences and dynamic data may be isolated from raw pixel comparison, with each exclusion named. No blanket percentage threshold may excuse moved controls, wrong typography, missing states or clipped content. Record discrepancies, repair them, and rerun the whole affected flow. Static XAML review or generated images cannot pass this gate.

Also verify 1500×1000, 1100×800 and 980×640, profile themes, expanded/collapsed log, long content, focus and disabled/error states. Discarded compact boards are NOT baselines. Where a smaller size requires rearrangement, retain both trees and obtain a separately approved responsive reference before marking that viewport complete; do not silently hide a tree or squeeze unreadable columns. The approved wide-screen work can proceed independently.

## Composition and theme

- Wide layout: **Namespaces | Saved templates | Author | Prepare/preview**, with resizable splitters and full-width activity log. At A's reference extent pane boundaries are approximately x=296, 566 and 1076; measure actual bounds against the raster, including splitters.
- Preserve existing InvestigationWindow connection toolbar, Watch, Settings, status bar, UTC/Local selector and Investigation Active/DLQ inspector. No new avatar/logo/app heading. Counts refresh while the library is open when refresh is enabled.
- One removable topic filter sits above both trees. Hide unrelated entities and library branches, retaining matching subscriptions and folder ancestors. Keep Add folder/Refresh/New at the bottom of the library pane, never in the namespace tree.
- Preserve existing Segoe UI/Consolas and shared palette: primary `#0069FA`, text `#17213D`, secondary `#627692`, canvas `#FAFCFF`, border `#CBD8E8`, divider `#DFE6EE`, selection `#DBEDFF`, editor `#1C2937`. Use existing profile accent resources and semantic status colors.
- Existing defaults remain 14-DIP body/controls, 12 metadata, 16 section heading, 24 primary title, 32 minimum button height, 3 corner radius, 16 vector icons, spacing 4/8/12/16/24. Retain existing focus/hover/pressed/disabled templates. Do not use superseded three-pane dimensions.
- Preserve author Body/Properties/Variables, JSON/Text, title and Save/Save as; preserve Prepare's Single/CSV, mapping, validation, rows, selected body/properties and bottom Review action.
- Resolved destination is plain read-only Send to. A picker appears only for unresolved/multiple/unavailable targets. Final review shows read-only profile, exact endpoint and entity.

## A — full workspace

![Approved workspace](approved/01-workspace.png)

## B — capture and properties

![Approved capture and properties](approved/03-capture-properties.png)

## C — variables, single inputs and CSV validation

![Approved inputs](approved/04-inputs-validation.png)

## D — review, scheduling, progress and results

![Approved dispatch](approved/05-send-schedule.png)

## E — cancellation, lifecycle and filter errors

![Approved lifecycle](approved/06-results-lifecycle.png)

## Explicit image corrections

| Board | Required correction |
|---|---|
| A | Blurred prepared amount is numeric `149.90`; authored token stays quoted `"$(Amount)"`. View results is enabled only for retained execution results. Count columns retain existing accessible labels/tooltips. |
| B | Capture warning names real excluded/normalized properties and requires acknowledgement when the contract requires it; generic warning copy is illustrative. Capture's Message ID “Generate new” is a read-only summary, not a second mode editor. Generated/Custom mode is editable only in Properties. |
| C | `none (required)`/`not applicable` are display states, not serialized defaults. Use an explicit default toggle/value editor. Generated variables are string; mapping cannot edit declarations. |
| D | Short sample IDs represent complete GUIDs. Target fields are read-only. Three-message review and five-message progress are separate examples. One scheduled instant is not recurrence. |
| E | Unknown dispatch outcome belongs in Status, not Payload. Expired receipts remain in history. Retry requires fresh confirmation. Local templates remain usable offline; only broker observations become unavailable. File conflicts use fingerprints, not illustrated modification times. |
| All | Use actual values and exact domain outcome labels; no collage headings/numbers in production. Existing vector family and correct spelling override generated glyph artifacts. |

## Stage/state coverage

| Stage | Required boards and proof |
|---|---|
| 1 | A/B/C/E: registration, all-saved opening, associations, both filter directions, clear filter, new/open/save/refresh, dirty/conflict/unavailable/offline, pixel-perfect wide rendering |
| 2 | B/C/D/E: Active/DLQ capture, single preparation, inferred/explicit destination, send/schedule, results/cancel, preserved namespace refresh and Investigation context |
| 3 | A/C/D/E: CSV all-row validation, mapping/defaults, selected preview, ambiguous target, batch Stop/partial results/cancel |
| 4 | A–E: independent plan/pixel-perfect review, full lifecycle, approved responsive references, all profile accents, accessibility/DPI and broker proof |

Also exercise same-style variants: loading/canceled validation, malformed CSV, unused columns, duplicate-ID acknowledgement, TTL/partition/session/size errors, stale destination/reconnect, empty filtered library, long names, draft guards and cancellation attempt cap. No new visual convention is implied.

Approval: **five-board design and pixel-perfect requirement accepted by user**. Implementation/render/accessibility proof: **Pending**. Previous seven-board review records are historical, not approval of discarded images. All active assets are repository-local copies of the exact approved set. No production UI was implemented in this documentation change.
