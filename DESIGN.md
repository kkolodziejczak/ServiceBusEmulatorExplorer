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
| Data table | implicit `DataGrid`; `CompactDataGridColumnHeader`, `CompactDataGridRow`, `CompactDataGridCell`, `CompactDataGridText`, `CompactDataGridEditingText`, `CompactDataGridCombo`, `CompactDataGridCheckBox` | header, row hover, selected, keyboard focus, editing, empty | Message lists, Workbench properties and variables | Approved (left-aligned refinement 2026-10-02) |
| Validation notice | `ValidationWarningFrame` + `ValidationWarningAction` | warning, with remedy action, unresolved, cleared | Workbench destination warnings; all new validation | Approved |
| Dialog text | `DialogHeading`, `WorkbenchHeading`, `DialogLabel`, `DialogCaption` | — | Dialogs, Workbench | Approved |
| Entity icon | `EntityIcon`, `CopyIcon` paths; `IconResources.xaml` geometries | — | Namespace tree, correlation copy | Approved |
| Scrollbar | implicit `ScrollBar`, `ScrollThumb`, `ScrollPageButton` | normal, hover, drag, both orientations | Every scrolling surface | Approved |
| Connection toolbar | `InvestigationWindow.xaml` top row | connected, disconnected, warning, disabled | Investigation and Workbench shells | Approved |
| Namespace tree and search | `InvestigationWindow.xaml` left pane | selected, filtered, empty, loading, counts unavailable | Investigation and Workbench | Approved |
| Message inspector | `InvestigationWindow.xaml` right pane; `JsonSyntaxColorizer` | read-only, edited, invalid JSON, search match | Investigation | Approved |
| Activity log footer | `InvestigationWindow.xaml` bottom | expanded, collapsed, UTC/Local | Investigation | Approved |
| Watch notification | `WatchNotificationWindow.cs` | hover, dismiss | Tray notifications | Approved |
| Active badge | inline in `InvestigationWindow.xaml` (literal colours) | — | Message rows, inspector | Approved look, not yet a style |
| Message Workbench workspace | `MessageLibraryPrototypeView.xaml` (dummy data) | compose, prepare, review, results, cancelled | Prototype | Approved prototype; production pending |
| Responsive Compose layout | [Layout controller](src/ServiceBusEmulatorExplorer.App/Investigation/MessageLibraryPrototypeView.Layout.cs), `ApplyEditorResponsiveLayout`; [visual contract](specs/message-library/responsive-layout.md) | wide: JSON plus inspector; medium: tabs and two property columns; small: tabs and one property column; Variables, editing, warning remedy | Message Workbench Compose | Approved 2026-10-02, DEC-022 |

### Operational tables

Header and body share one column definition and one cell padding. Leading selection, status
and action columns are capped; the final data column fills the remaining width. Text values
keep their full content with a tooltip; they are never shortened in the model. Utility
checkboxes and action icons stay centered, everything else is left-aligned.

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
