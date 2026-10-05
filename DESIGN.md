---
# Machine-readable tokens. Every value below has exactly one implementation key in
# src/ServiceBusEmulatorExplorer.App/Investigation/Resources/SharedStyles.xaml (x:Key).
# Reference tokens in prose as {colors.primary}. Sizes are WPF DIPs.
version: 1
name: Service Bus Emulator Explorer
colors:
  primary: "#0069FA"
  primary-hover: "#005BD8"
  primary-pressed: "#004FBD"
  ink: "#17213D"
  secondary: "#627692"
  action-text: "#234266"
  canvas: "#FAFCFF"
  chrome: "#F5F9FE"
  raised: "#FFFFFF"
  subtle-surface: "#F3F7FC"
  column-header: "#EEF4FA"
  control-border: "#CBD8E8"
  divider: "#DFE6EE"
  neutral-hover: "#EAF4FF"
  selection: "#DBEDFF"
  neutral-pressed: "#C7E2FF"
  icon: "#52708D"
  entity-primary: "#087BC1"
  subscription: "#7351B5"
  disabled-bg: "#F1F4F8"
  disabled-border: "#DCE3EC"
  disabled-text: "#738196"
  dead-letter: "#FFE6C8"
  dead-letter-text: "#A65B00"
  modified: "#FFE38D"
  modified-text: "#5E4600"
  destructive: "#D11A2A"
  destructive-hover: "#FFF0F1"
  destructive-pressed: "#FFDADD"
  destructive-pressed-text: "#B81020"
  health-connected: "#17823B"
  health-warning: "#A56700"
  health-disconnected: "#C83B3B"
  editor-bg: "#1C2937"
  editor-fg: "#DCE7F3"
  editor-line-number: "#8394A9"
  dark-button: "#253546"
  dark-button-border: "#526578"
  dark-button-hover: "#344B63"
  dark-button-pressed: "#182432"
  dark-button-disabled-text: "#99A9BB"
typography:
  body: { size: 14, weight: 400 }
  meta: { size: 12, weight: 400 }
  badge: { size: 11, weight: 400 }
  section-title: { size: 16, weight: 600 }
  compact-title: { size: 18, weight: 600 }
  dialog-title: { size: 24, weight: 600 }
spacing: [0, 1, 3, 4, 7, 8, 12, 16, 24]
rounded: { control: 3, surface: 4, notification: 9 }
---

# DESIGN.md — Service Bus Emulator Explorer

## Overview

A dense desktop operations tool for Azure Service Bus, in the workflow tradition of
paolosalvatori/ServiceBusExplorer but with a modern, flat visual treatment: compact connection
toolbar, namespace tree, message list and inspector panes, a full-width activity log and a
bottom time selector. One profile-selected accent colour carries primary actions and selection;
every other colour has a fixed meaning. Semantic colours (connection health, dead-letter amber,
modified yellow, destructive red) never change with the profile. Three rules define the system:
every interactive control shows explicit normal, hover, pressed, focused, selected and disabled
states; keyboard focus is visible but pointer clicks leave no frame; destructive actions are
separate, visibly red, and confirmed.

## Colors

The authoritative list is the brush section at the top of `SharedStyles.xaml`. `ProfileTheme`
recomputes the accent-derived brushes when a connection profile with a non-blue colour is
active; the values below are the blue baseline.

| Token | Resource key | Value | Use |
| --- | --- | --- | --- |
| `{colors.primary}` | `PrimaryBrush` | #0069FA | Primary buttons, selected tab underline, active profile accent. Chosen for ≥ 4.5:1 contrast with white labels. |
| `{colors.primary-hover}` / `{colors.primary-pressed}` | `PrimaryHoverBrush` / `PrimaryPressedBrush` | #005BD8 / #004FBD | Primary button states. |
| `{colors.ink}` | `InkBrush` | #17213D | Body text, titles, table values. Never pure black. |
| `{colors.secondary}` | `SecondaryBrush` | #627692 | Captions, metadata, inactive labels, scrollbar thumb. |
| `{colors.action-text}` | `ActionTextBrush` | #234266 | Column headers and action labels. |
| `{colors.canvas}` / `{colors.chrome}` / `{colors.raised}` | `CanvasBrush` / `ChromeBrush` / `RaisedBrush` | #FAFCFF / #F5F9FE / #FFFFFF | Window background / toolbar / cards and table rows. |
| `{colors.subtle-surface}` / `{colors.column-header}` | `SubtleSurfaceBrush` / `ColumnHeaderBrush` | #F3F7FC / #EEF4FA | Grouped panels / grid headers. |
| `{colors.control-border}` / `{colors.divider}` | `ControlBorderBrush` / `DividerBrush`, `ToolbarDividerBrush` | #CBD8E8 / #DFE6EE | 1 DIP control borders / pane and row separators. The toolbar divider is always neutral, never the accent. |
| `{colors.neutral-hover}` / `{colors.selection}` / `{colors.neutral-pressed}` | `NeutralHoverBrush` / `SelectionBrush` / `NeutralPressedBrush` | #EAF4FF / #DBEDFF / #C7E2FF | Row and item hover / selected / pressed. Hover is a background change, never opacity. |
| `{colors.icon}` | `IconBrush` | #52708D | Vector action icons. |
| `{colors.entity-primary}` / `{colors.subscription}` | `EntityPrimaryBrush` / `SubscriptionBrush` | #087BC1 / #7351B5 | Queue and topic glyphs / subscription glyphs. Distinct geometry as well as colour. |
| `{colors.disabled-*}` | `DisabledBackgroundBrush`, `DisabledBorderBrush`, `DisabledTextBrush` | #F1F4F8 / #DCE3EC / #738196 | Disabled controls. |
| `{colors.dead-letter}` / `{colors.dead-letter-text}` | `DeadLetterBrush` / `DeadLetterTextBrush` | #FFE6C8 / #A65B00 | DLQ badges and rows; always with a text label. |
| `{colors.modified}` / `{colors.modified-text}` | `ModifiedBrush` / `ModifiedTextBrush` | #FFE38D / #5E4600 | Edited or dirty state. |
| `{colors.destructive*}` | `DestructiveBrush`, `DestructiveHoverBrush`, `DestructivePressedBrush`, `DestructivePressedTextBrush` | #D11A2A … | Delete and discard only; outline button, never a red fill. |
| `{colors.health-*}` | `ConnectedHealthBrush`, `WarningHealthBrush`, `DisconnectedHealthBrush` | #17823B / #A56700 / #C83B3B | Connection indicator, always labelled. |
| `{colors.editor-*}` | `EditorBackgroundBrush`, `EditorForegroundBrush`, `EditorLineNumberBrush` | #1C2937 / #DCE7F3 / #8394A9 | Dark JSON editor and activity log; syntax colours from `JsonSyntaxColorizer`. |
| `{colors.dark-button*}` | `DarkButtonBrush` … | #253546 … | Buttons placed on the dark editor surface. |

A colour that is not a brush in `SharedStyles.xaml` does not exist in the app. A hex literal in
a view is a defect; add a brush with a semantic name first. Two known exceptions still waiting
for brushes: the Active badge triplet (#EFF6FF fill, #6C9BD2 border, #174A7E text) and the
validation warning pair (#FFF5E3 / #D9A441) used by `ValidationWarningFrame`.

## Typography

Segoe UI everywhere; Consolas for JSON and the console. Sizes are role-based; a view never picks
a size, it picks a style.

| Token | Resource key | Size / weight | Use |
| --- | --- | --- | --- |
| `{typography.body}` | implicit `Button`, `TabToggle`, default `TextBlock` | 14 / 400 | Controls, body text, table values, JSON |
| `{typography.meta}` | `DialogCaption`, console text | 12 / 400, secondary colour | Metadata, timestamps, captions, console |
| `{typography.badge}` | (no style yet) | 11 / 400 | Badges and timestamps inside rows only |
| `{typography.section-title}` | (no style yet) | 16 / 600 | Section titles inside a pane, notification summary |
| `{typography.compact-title}` | (no style yet) | 18 / 600 | Titles at the compact window size |
| `{typography.dialog-title}` | `DialogHeading`, `WorkbenchHeading` (bold) | 24 / 600 | One per dialog or inspector |
| label | `DialogLabel` | 14 / 600 | Form field labels |

The Settings window uses the native title bar and no duplicate content heading.

## Layout

Spacing scale: 4, 8, 12, 16, 24. Two fixed exceptions: 7 for the icon-to-label gap inside
buttons and 1 or 3 for hairlines and focus insets. Every `Margin` and `Padding` in a view uses a
scale value; component padding lives in the style (`Button` 12,7; `TabToggle` 12,8; grid cells
8,0).

| Rule | Value |
| --- | --- |
| Button minimum height | 32; compact footer selector 24; icon-only target 28 × 28 |
| Toolbar height | 40 |
| Message row height | 54 desktop, 50 compact |
| Vector icon | 16 optical box, 1.5 stroke (Copy keeps 1.4 and 16 × 18); chevrons 8 × 5 |
| Scrollbar | 18 hit lane, 8 visible rounded thumb, transparent track over dark surfaces |
| Supported windows | 1500 × 1000 desktop, 1100 × 800 compact, 980 × 640 minimum |
| Windows scaling to verify | 100 %, 125 %, 150 %, 200 % and multi-monitor moves |

Layouts stay responsive: no fixed-width rows or oversized minimum widths; compact fields shrink,
trim text or scroll inside the field rather than forcing the shell to scroll horizontally. At
minimum size with the log expanded only one full message row may be visible; that trade-off is
accepted and must be tested, not hidden.

## Elevation

Flat. Separation comes from `DividerBrush` hairlines and surface tints. The only raised surface
is the Watch notification window's rounded outer panel.

## Shapes

Controls 3; menus and hover surfaces 4; notification outer panel 9; scrollbar thumb 4.

## Components

A component exists when it has a row here and a style in `SharedStyles.xaml` or
`DropdownStyles.xaml`, both merged by `App.xaml`. States listed are the ones the style defines.
Proposals and their mockups live in `specs/ui-language.md` and
`specs/message-library/design/`; a component moves here when the user approves the rendered
result.

| Component | Implementation / canonical source | States | Where used | Status |
| --- | --- | --- | --- | --- |
| Button | implicit `Button` style; `KeyboardActionFocusVisual` | normal, hover, pressed, keyboard focus (bottom cue, no frame after pointer click), disabled | Every surface | Approved |
| Primary button | `PrimaryButton` | as Button, accent fill, white label | One main action per dialog or stage | Approved |
| Destructive button | `DestructiveButton` | as Button, red outline, tinted hover and pressed | Delete, Discard, always followed by a confirmation | Approved |
| Dark button | `DarkButton` | as Button on the editor surface | Inspector and console actions | Approved |
| Icon button | `IconButton` (28 × 28, transparent) | as Button | Toolbar and row actions with tooltip and automation name | Approved |
| Tab toggle | `TabToggle` (3 DIP accent underline) | normal, hover, checked, keyboard focus | Shell workspaces, Settings tabs, inspector tabs | Approved |
| Dropdown | implicit `ComboBox` → `AppComboBox`; `CompactComboBox`; `AppComboBoxItem` | closed, open, hover, selected item, disabled | All selectors; Settings and Workbench no longer have own templates | Approved |
| Investigation row cell | `InvestigationMessageCell` based on `CompactDataGridCell` | inherits row hover/selected/normal background; existing keyboard cue | Investigation message lists only | Approved DEC-039 |
| Data table | implicit `DataGrid`; `CompactDataGridColumnHeader`, `CompactDataGridRow`, `CompactDataGridCell`, `CompactDataGridText`, `CompactDataGridEditingText`, `CompactDataGridCombo`, `CompactDataGridCheckBox` | header, row hover, selected, keyboard-only bottom focus cue (no cell frame on pointer focus), editing, empty | Message lists, Workbench properties and variables | Approved (left-aligned refinement 2026-10-02) |
| Validation notice | `ValidationWarningFrame` + `ValidationWarningAction` | warning, with remedy action, unresolved, cleared | Workbench destination warnings; all new validation | Approved |
| Dialog text | `DialogHeading`, `WorkbenchHeading`, `DialogLabel`, `DialogCaption` | — | Dialogs, Workbench | Approved |
| Entity icon | `EntityIcon`, `CopyIcon` paths; `IconResources.xaml` geometries | — | Namespace tree, correlation copy | Approved |
| Scrollbar | implicit `ScrollBar`, `ScrollThumb`, `ScrollPageButton` | normal, hover, drag, both orientations | Every scrolling surface | Approved |
| Connection toolbar | `InvestigationWindow.xaml` top row | connected, disconnected, warning, disabled | Investigation and Workbench shells | Approved |
| Namespace tree and search | `InvestigationWindow.xaml` left pane | selected, filtered, empty, loading, counts unavailable | Investigation and Workbench | Approved |
| Namespace Watch and refresh indicators | `NamespaceActiveCount`, `NamespaceDlqCount`, `NamespaceRefreshIndicator`, `NamespaceIndicatorLegendIcon` in shared styles; `InvestigationWindow.Indicators.cs` | watched Active/DLQ count underline; persistent watched-entity refresh glyph; current un-watched Investigation row refresh glyph; muted pause glyph; hidden when off or disconnected | Namespace tree | Approved 2026-10-03, DEC-027 and DEC-028 |
| Message location hierarchy | `MessageLocationTemplate`, `MessageLocationIcon`, `MessageLocationText` | topic/subscription, queue, independently trimmed lines with full tooltip; state remains visible | Investigation browse/search | Approved Location 37 A, DEC-033 |
| Message inspector | `InvestigationWindow.xaml` right pane; `JsonSyntaxColorizer` | read-only, edited, invalid JSON, search match | Investigation | Approved |
| Inspector modified tab | `InspectorModifiedBadge`, `InspectorModifiedText` inside `TabToggle` | clean hidden; edited badge inside shared selected background and underline | Investigation | Approved Inspect 33 |
| Inspector discard link | `InspectorDiscardLink` based on `IconButton` | hover, keyboard focus, disabled; restores local body draft | Investigation | Approved Inspect 33 |
| Inspector editability status | `InspectorEditabilityStatus`, `InspectorStatusText`, pencil/lock geometry | Editable for DLQ Body, Read-only for Active/Properties or pending replay; no message hidden | Dark document viewport bottom-right overlay | Approved Inspect 40 |
| Message column selector | `ColumnsPopup`, shared buttons, checkbox, caption, search and scrollbar styles; `ColumnsGeometry` | search, checked, last column disabled, no results, reset, dismiss and return focus | Single queue/subscription Active and DLQ; global saved selection | Approved Columns 38/41 |
| Replay review and history | `ReplayWindow`, `ReplayReviewWindow`, `ReplayHistoryWindow`; existing dialog text, validation, buttons and compact data grid | editable exact-ID review, cancel, invalid ID; modeless saved history, selected inline details, observed state counts, pending actions, disconnected | Investigation | Approved proposal 29, Replay 32 and Replay 47 (DEC-044) |
| Replay 47 visual hierarchy | `ReplayHistoryGrid`, `ReplayHistoryHeading`, `ReplayHistoryMetadata`, `ReplayHistoryIdentity`, `ReplayHistoryBadge`, `ReplayHistoryBadgeText`, `ReplayHistoryDetailId` | 16 DIP table values/counts, 18 DIP summary, 14 DIP secondary text/full IDs; blue Active, existing pale-orange/dark-orange DLQ, neutral absent, plain unchecked; adjacent Copy and inline source | Replay history only | User correction of the first implementation to the approved Replay 47 image |
| Replay identity badge | `MessageReplayBadge`, `MessageReplayBadgeText` | hidden for ordinary/not-sent IDs; exact saved replay ID with original/time tooltip; independent Active/DLQ badge | Browse and search rows | Approved Replay 35, DEC-032 |
| Replay history action | existing outlined Button with `HistoryGeometry`, immediately before Settings | label hidden at existing compact breakpoint; tooltip and accessible name retained | Investigation toolbar | Approved Replay 34/35, DEC-032 |
| JSON folding visuals | `JsonFoldButton`, `JsonFoldingMargin`, `JsonFoldPlaceholder` | expanded/down, collapsed/right, hover, pressed, keyboard focus; dark rounded ellipsis | Shared inspection editor | Approved Inspect 36, DEC-032 |
| Activity log footer | `InvestigationWindow.xaml` bottom | expanded, collapsed, UTC/Local | Investigation | Approved |
| Watch notification | `WatchNotificationWindow.cs` | hover, dismiss | Tray notifications | Approved |
| Active badge | inline in `InvestigationWindow.xaml` (literal colours) | — | Message rows, inspector | Approved look, not yet a style |
| Message Workbench workspace | `MessageLibraryPrototypeView.xaml` (dummy data) | compose, prepare, review, results, cancelled | Prototype | Approved prototype; production pending |
| Responsive Compose layout | [Layout controller](src/ServiceBusEmulatorExplorer.App/Investigation/MessageLibraryPrototypeView.Layout.cs), `ApplyEditorResponsiveLayout`; [visual contract](specs/message-library/responsive-layout.md) | wide: JSON plus inspector; medium: tabs and two property columns; small: tabs and one property column; Variables, editing, warning remedy | Message Workbench Compose | Approved 2026-10-02, DEC-022 |
| Workbench contextual search | [Contract](specs/message-library/search-and-inspector-tabs.md); `SearchSuggestionGroupHeaderTemplate`, `SearchSuggestionListItem`, `SearchSuggestionCaption` in shared styles | typing, grouped results, hover, keyboard selection, empty, dismissed | Workbench namespace and template searches | Approved 2026-10-02, DEC-023 |
| Scheduling inputs | [Contract](specs/message-library/scheduling-controls.md); CalendarOnlyDatePicker with ScheduleDatePicker, ScheduleCalendar and ScheduleChoiceRadioButton in shared SchedulingStyles.xaml | closed, expanded, selected day, today, hover, pressed, keyboard focus, disabled; constrained hour/minute and validation | Workbench Review | Approved 2026-10-02, DEC-024 |

Inspector refinement (Inspect 33): Find uses a full-width light row above the document. Copy stays at the top-right of the visible document viewport, appears on document hover/focus, and stays in view while scrolling (DEC-037 supersedes the original Inspect 33 scroll-away behavior). Keyboard focus preserves the scroll position. Body and Properties reserve a right action lane to keep long text clear of Copy. JSON folding leaves document text and undo unchanged; Ctrl+Shift+[ toggles the innermost containing fold, and Find expands folded matches. Preserve all inspector metadata and actions. The Modified badge uses existing semantic palette resources; its rounded shape and dot are approved by Inspect 33.

Replay 35 reserves state-badge space in the Location / State cell: shorten the source first and retain its full tooltip. Replay is provenance from this profile/namespace's saved attempt, not a delivery state or an API-success claim. Inspect 36 uses one gutter column of unboxed chevrons and existing editor/dark-button brushes; each fold has an accessible action name and retains the existing keyboard command. Folding styling is shared and does not introduce user settings.

### Operational tables

Investigation startup reuses the existing empty panels before preferences or connection work completes. Never show blank message metadata or an editable document without a selected message. Namespace discovery and empty connections must not use search-no-match wording unless a filter or search is active. The empty inspector uses existing editor foreground and line-number brushes for readable text on its dark surface.

Header and body share one column definition and one cell padding. Leading selection, status
and action columns are capped; the final data column fills the remaining width. Text values
keep their full content with a tooltip; they are never shortened in the model. Utility
checkboxes and action icons stay centered, everything else is left-aligned.

Approved Investigation exception (DEC-042, Replay 45 #4-5): every real message-grid column
uses content sizing within responsive caps, independent of order. Keep correlation Copy
adjacent to its left-aligned value. Only empty trailing space fills surplus width; it is not
a selectable, sortable, configurable or persisted data column. Preserve pinned columns and
horizontal scrolling when content exceeds the viewport.

Approved scope composition (DEC-041, Location 44): reuse MessageLocation icons/text and the
namespace tree SelectionBrush. Subscription headers show topic above indented subscription;
topic headers show All subscriptions. The sidebar highlights the browse scope through focus
changes and refresh, independently of the inspected message. Search keeps its own identity.

Replay 47 (DEC-044) refines Replay 45 v2 (DEC-043): Original message/search, Replay/search
with sent/requested time underneath, and Observed copies with state counts (DLQ first)
and checked date/time, followed by compact eligible history trash. Only the selected row
expands inline, containing full IDs and Copy, source and original state with Delete original
beside it and applicable failure/coverage reasons. Destination remains on the source tooltip, without a redundant fourth row. Remove the permanent bottom
panel and Scan details disclosure. The table owns scrolling; details wrap naturally at
compact widths without an inner scrollbar or fixed three-line cap. Preserve full dates,
date-format preferences, zones and truthful incomplete/uncertain/not-checked states.
The user rejected the first implementation's small pale pills, stretched Copy positions and
extra Destination row. Follow the approved image's visual hierarchy using the scoped
ReplayHistory styles above, existing action/entity geometries, DestructiveButton and
ProfileWarningWindow. Source hierarchy reads horizontally and wraps at compact widths.
Copy follows each full ID; it may wrap below a long ID. Default window is 1200x720,
with 780x480 still supported. Search and cleanup behavior are unchanged.

### Actionable validation

Show cause and remedy in the severity-coloured frame with the action button on its right in the
same colour family. The action navigates to the screen or tab, expands and scrolls the target
into view and focuses the exact field while preserving drafts and selection. Validation stays
visible until revalidation succeeds; a condition with no in-app remedy explains the next step
instead of showing a dead-end button.

## Do's and Don'ts

- Do put a component's appearance and states in `SharedStyles.xaml`, implicit for standard
  controls and `BasedOn` variants for intentional differences; remove the local template from
  the surface you touch.
- Do verify normal, hover, pressed, keyboard focus, selected and disabled after every style
  change, and that a new instance inherits the shared style.
- Do give every action a visible label or a tooltip plus a stable automation name.
- Don't use opacity-only hover, lingering focus frames after pointer clicks, or Unicode glyphs
  in place of vector icons.
- Don't add numeric badges, duplicate headings, or a second connection picker; the native title
  bar carries the app name.
- Don't restyle screens the task did not touch; migrate one component at a time and lower the
  check baseline as you go.
- Don't treat a generated mockup as approval; approval is the user's word on the rendered result.

## Workbench cleanup approved 2026-10-03

Namespace indicators follow [proposal 25](specs/message-library/design/proposed/25-watch-and-auto-refresh-indicators.png): semantic count colors and alignment stay unchanged; a thin underline marks effective Watch inclusion on queue/subscription Active and DLQ counts, including inherited rules. Scheduled and topic aggregate counts are never underlined. The existing 14-DIP refresh/pause vector sits beside watched entity names and the current Investigation entity name, without animation or a new click target. Its tooltip states the shared interval, Active/DLQ scope and background/current-view role. Explicit pause preserves independent Watch underlines; Off and disconnect hide refresh markers. Watched entities retain their markers and refresh in search and Workbench. A topic marker describes its watched subscriptions, respecting child exclusions. The interval remains window-wide and namespace counts still refresh globally. A compact second legend row explains both signals. Existing fonts, icon geometries and theme tokens remain authoritative over generated mockup scaling.

| Component | Contract and states | Approved visual |
| --- | --- | --- |
| Content-sized Workbench dialogs | Map CSV inputs keeps all four mapping columns and generated-field notice, removes the redundant Input sources helper sentence, and sizes to content with compact spacing below its actions. Recheck other Workbench dialogs for unused fixed-height space; validation expansion must remain visible, with bounded scrolling only when content exceeds the available screen. Preserve actual WPF fonts and controls over generated scaling. | [Mapping proposal](specs/message-library/design/proposed/23-content-sized-mapping-dialog.png) |
| Namespace filter and default workspace | Applied filter uses the existing search field with a primary outline, funnel and clear action; clearing returns the neutral magnifier state. No extra scope chip. Queue/topic/subscription counts share right-aligned columns independent of indentation; preserve bottom legend and full-value tooltips. Restore the shared rounded Copy icon. Activity log starts collapsed on each app launch; toggling remains available during the session. | [Proposal 21](specs/message-library/design/proposed/21-namespace-and-default-workspace.png) |
| Validation, run results and clear log | Valid input before a run has no results action in its validation banner. Invalid input offers View errors; retained execution results use a separate View last run action beside Review in the Prepare footer. Expanded Activity log groups a labelled Clear log button with the collapse control; disabled when empty. Clear removes log entries only. Existing flat tokens, original Copy geometry and current UI text remain authoritative over incidental generated variations. | [Proposal 22](specs/message-library/design/proposed/22-validation-results-and-activity-log.png) |

Date format preference (DEC-029): reuse General Settings rows, AppComboBox and DialogCaption. Scheduling labels use the same choice and stay content-sized and calendar-only. [Contract and approved proposal](specs/date-display.md).

Investigation inspector (DEC-030): Body and Properties tabs stay left; Create template (AddGeometry icon plus label) and Replay stay right. If their measured widths cannot fit together, actions use a second right-aligned row. Raw tab is removed; formatting and binary/plain-text fallback stay in Body. Reuse TabToggle, Button and PrimaryButton.

Location 37 A (DEC-033) uses shared MessageLocationTemplate, MessageLocationIcon and MessageLocationText styles: existing topic icon and muted topic above the existing subscription icon and darker subscription, indented 8 DIP. Each line trims independently with the full typed location in the tooltip and accessible name. Queue rows show one queue icon/name without an empty topic line. Keep existing row heights and state badges. DEC-034 refines correlation visibility: show the existing Correlation ID column and row Copy for a single queue/subscription (Active or DLQ); hide them when Location / State is shown for search or topic-wide browsing. The inspector retains its correlation value, Copy and Find related throughout. Replay searches describe distinct IDs, deliveries and receiving entities, marking incomplete results as found so far alongside the existing scan status.

Columns 38 (DEC-035): use the registered compact table, WatchCheckStyle, search icon/field, IconButton, PrimaryButton and link treatment in a bounded popup. One shared saved selection applies immediately to single-entity Active/DLQ lists. Missing values show an em dash; optional columns scroll inside the list. Preserve the default responsive layout for legacy settings and the existing search/topic-wide layout. Keep selection checkboxes and at least one data column.

Inspect 40 (DEC-036): InspectorStatusText uses the existing editor foreground and caption typography. PencilGeometry and LockGeometry accompany Editable/Read-only as a bottom-right document viewport overlay. Status is non-interactive and hidden without a selected message; tooltip/accessibility help explains direct editing or the read-only reason. Preserve all prior inspector interactions.

Columns 41 (DEC-038): a mandatory 64 DIP State column immediately after the 40 DIP selection column reuses the existing Active/DLQ badges in every Investigation row. Freeze both leading columns. Location retains only its hierarchy/icons. Single-entity data headers support dragging; the selector groups enabled choices in display order with registered IconButton up/down chevrons, accessible move names and disabled end controls. Both paths save one shared order independent of visibility. Hidden columns retain their slot; newly enabled columns append; Reset defaults restores selection and order. Search/topic data columns remain fixed.

DEC-039: visible column order determines width behavior, never the property ID. Pinned selection/State retain fixed widths; earlier data columns use Auto content sizing with a shared responsive 120-200 DIP cap; the final visible data column uses star fill without a maximum. Re-evaluate on reordering, toggles, scope changes and resize. Editor overlays use the existing text/icon/button resources: Copy top-right and persistent editability bottom-right, both inset 24 DIP from the edge beyond the 18 DIP scrollbar. The editor fills the whole viewport; no status row. Both Body and Properties reserve a 128 DIP right text lane.
