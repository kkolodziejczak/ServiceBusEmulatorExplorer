# Decisions

What the user decided and why. One entry per decision, numbered, never edited into something
else: a changed decision gets a new entry that supersedes the old one. Accepted decisions are
binding for agents; a task that conflicts with one stops and says so.

Record a decision when the user states how something must look, behave or be built **and gives a
reason**, or rejects an approach. Routine instructions are not decisions. Keep each entry under
ten lines. Quote the user's reason in their words where possible. Entries marked *inferred* were
reconstructed from AGENTS.md, `specs/ui-language.md`, `Lessons/` and commit history on
2026-10-02; the user should correct their reasons.

When asked "why is X like this", answer from the matching entry and cite its ID.

| ID | Status | Scope | Decision |
| --- | --- | --- | --- |
| DEC-001 | accepted | DLQ | Replay is non-destructive by default |
| DEC-002 | accepted | DLQ | Delete is separate, visibly destructive, typed confirmation, captured DLQ deliveries only |
| DEC-003 | accepted | actions | Intent-specific labels: Send New Message, Replay DLQ Copy, Edit and Replay DLQ |
| DEC-004 | accepted | time | Timestamps explicit UTC; display offers UTC or Local only |
| DEC-005 | accepted | product | Workflow reference is paolosalvatori/ServiceBusExplorer; visual treatment modern, not a clone |
| DEC-006 | accepted | styling | Shared styles: implicit defaults plus BasedOn variants, migrated incrementally |
| DEC-007 | accepted | focus | Keyboard focus shows a cue; pointer clicks leave no frame |
| DEC-008 | accepted | colour | Primary blue #0069FA for ≥ 4.5:1 contrast with white |
| DEC-009 | accepted | colour | Profile colour tints chrome; semantic colours stay fixed |
| DEC-010 | accepted | chrome | Toolbar divider neutral gray, no accent line |
| DEC-011 | superseded by DEC-012 | tables | Centered headers and values |
| DEC-012 | accepted | tables | Left-aligned compact tables, capped leading columns |
| DEC-013 | accepted | validation | Validation shows cause and a remedy action that navigates and focuses |
| DEC-014 | accepted | process | Approved mockups are implemented pixel-perfect; a mockup is not approval |
| DEC-015 | accepted | icons | Vector icons only, 16 box, 1.5 stroke; no Unicode glyph substitutes |
| DEC-016 | accepted | settings | No duplicate connection picker; auto-connect on profile switch is opt-in |
| DEC-017 | accepted | actions | Compact icon-plus-label action strips with tooltips and stable automation names |
| DEC-018 | accepted | commands | Hide inapplicable commands; show blocked ones disabled with an explanation |
| DEC-019 | accepted | build | Package versions pinned |
| DEC-020 | accepted | docs | README is user-facing; maintainer mechanics live in AGENTS.md |
| DEC-021 | accepted | layout | No numeric navigation badges; native title bar carries the app name |
| DEC-022 | accepted | Workbench layout | Responsive JSON/inspector with two property columns at medium width and one at small width |
| DEC-023 | accepted | Workbench search and navigation | Contextual suggestions and adjacent inspector tabs |
| DEC-024 | accepted | Workbench scheduling | Styled calendar and constrained hour/minute selectors |
| DEC-025 | width superseded by DEC-026 | Workbench scheduling | Compact date width and vertically centered date text |
| DEC-026 | accepted | Workbench scheduling | Content-sized date field with calendar-only selection |
| DEC-027 | accepted | Namespace tree | Independent Watch count underlines and current-view refresh/pause indicators |
| DEC-028 | accepted | Background refresh | Watched scopes refresh independently of selection using one shared interval |
| DEC-029 | accepted | Date display | App-wide Windows default with five selectable formats |
| DEC-030 | accepted | Inspector | Body and Properties tabs; right-aligned template and replay actions |
| DEC-031 | accepted | Inspector and replay | Foldable body and explicit replay review/history |
| DEC-032 | accepted | Investigation presentation | Visible replay/state badges and app-themed JSON folding |
| DEC-033 | accepted | Investigation table | Two-line location hierarchy; correlation stays in inspector |
| DEC-034 | accepted | Investigation table | Correlation column follows single-entity versus multi-location view |
| DEC-035 | accepted | Investigation columns | Global saved column choices with immediate preview |
| DEC-036 | accepted | Inspector | Subtle Editable or Read-only status, direct editing preserved |
| DEC-037 | accepted | Inspector Copy | Hover Copy stays anchored to the visible viewport while scrolling |

| DEC-038 | accepted | Investigation columns | Pinned State and shared saved order via headers or selector |

| DEC-039 | accepted | Investigation layout | Order-based column sizing and viewport editor overlays |

| DEC-040 | accepted | Investigation deletion | Delete only explicitly checked messages |
| DEC-041 | accepted | Investigation scope | Hierarchical header and persistent sidebar scope selection |
| DEC-042 | accepted | Investigation columns | Compact real columns with an empty trailing remainder |
| DEC-043 | accepted | Replay history | Searchable original and replay IDs with row-local history removal |
| DEC-044 | accepted | Replay history | Selected-row inline details replace the permanent bottom panel |

## DEC-001 — Replay is non-destructive by default

- Status: accepted · 2026-07-08 · Scope: DLQ
- Decision: replaying a dead-lettered message creates a new active message and leaves the original in the DLQ until a separate, confirmed delete.
- Because (inferred from `Lessons/`): an explorer tool must never lose the evidence of a failure while trying to recover from it.
- Consequences: replay and delete are different commands and different services; a replay whose DLQ-lock abandon fails is still reported as a successful send.

## DEC-002 — Delete is separate, visibly destructive and scoped

- Status: accepted · 2026-09-12 · Scope: DLQ
- Decision: delete is a red outline action with exact typed `DELETE` confirmation; multi-delete and visible-page delete confirm explicitly; production delete is restricted to captured DLQ deliveries, and mixed Active selections disable it.
- Because (inferred from `specs/ui-language.md` and AGENTS.md): deletion is irreversible and the emulator can mis-report counts, so only messages the tool has actually seen may be targeted.
- Consequences: `DestructiveButton` style; checked messages form the Investigation batch, with no focused-row fallback (updated by DEC-040); cancellation after settlement still reports confirmed per-delivery outcomes.

## DEC-003 — Intent-specific action labels

- Status: accepted · 2026-07-08 · Scope: message actions
- Decision: `Send New Message` for blank authoring, `Replay DLQ Copy` for cloning without edits, `Edit and Replay DLQ` for edited replay.
- Because (inferred from `Lessons/`): overlapping legacy terms (repair, resubmit, replay copy) on one surface made it unclear what would happen.
- Consequences: new actions get a label that names the outcome, not the mechanism.

## DEC-004 — Explicit UTC timestamps, UTC or Local display only

- Status: accepted · 2026-09-12 · Scope: time
- Decision: operation-log and message timestamps are explicit UTC in the model; the footer offers UTC or Local and nothing else.
- Because (inferred from `Lessons/`): enqueue, expiry, created and log times are compared to reason about ordering and failures; ambiguous zones make that reasoning wrong.
- Consequences: `IClock` injected; no third time-zone option in Settings.

## DEC-005 — Workflow reference, modern treatment

- Status: accepted · 2026-07-08 · Scope: product
- Decision: the workflow follows paolosalvatori/ServiceBusExplorer (toolbar, namespace tree, entity view, message list and detail, action strip, log pane) with a modern flat visual treatment.
- Because (inferred from `Lessons/`): the user already knew that tool's workflow; the earlier Minimal API reference was illustrative, not an architecture constraint.
- Consequences: ask whether any new reference project is authoritative or illustrative before adopting its structure.

## DEC-006 — Shared styles, migrated incrementally

- Status: accepted · 2026-09-27 · Scope: styling
- Decision: approved appearance and states live in `SharedStyles.xaml` as implicit styles for standard controls and `BasedOn` variants for intentional differences; local templates are removed from the surface a task touches, and untouched screens are not restyled.
- Because (inferred from AGENTS.md): a whole-app restyle was rejected as too risky; consistency is reached one component at a time.
- Consequences: the convention check tolerates existing literals through a baseline and fails only on new ones.

## DEC-007 — Keyboard focus cue, no pointer frame

- Status: accepted · 2026-09-23 · Scope: focus
- Decision: buttons and button-like toggles show a bottom focus cue during keyboard navigation and no lingering frame after a pointer click.
- Because (inferred from commit "Remove lingering pointer focus frames from workbench actions"): the frame read as a stuck state after clicking.
- Consequences: `KeyboardActionFocusVisual`; a colour-only border trigger on a borderless icon button is not an acceptable focus cue.

## DEC-008 — Primary blue chosen for contrast

- Status: accepted · 2026-09-12 · Scope: colour
- Decision: one primary palette based on #0069FA, hover #005BD8, pressed #004FBD.
- Because (inferred from the ui-language audit): the earlier #0078F8 and #087CF0 gave about 4.1:1 against white 14 DIP labels; #0069FA gives about 4.8:1.
- Consequences: hover and pressed are darker fills, never a faded whole button.

## DEC-009 — Profile colour tints chrome, semantics stay fixed

- Status: accepted · 2026-09-12 · Scope: colour
- Decision: the selected connection profile colour (blue, purple, teal, orange or red) drives the accent and tints surfaces and selection across the shell, Settings and related windows; connection health, DLQ amber and destructive red never change.
- Because (inferred from `specs/ui-language.md`): the profile colour exists to tell environments apart at a glance, so it must be visible everywhere, but safety colours must mean the same thing in every profile.
- Consequences: `ProfileTheme.Apply` recomputes accent-derived brushes; editing an inactive profile does not preview its theme.

## DEC-010 — Neutral toolbar divider

- Status: accepted · 2026-09-12 · Scope: chrome
- Decision: a 1 DIP neutral gray divider below the toolbar, meeting the pane splitters, with no accent line.
- Because (inferred from commit "Remove toolbar accent line and retain neutral divider"): the accent line competed with selection and the profile colour.
- Consequences: `ToolbarDividerBrush` stays neutral in every profile.

## DEC-012 — Left-aligned compact tables

- Status: accepted · 2026-10-02 · Scope: tables · Supersedes: DEC-011
- Decision: headers, values and editors are left-aligned; leading selection, status and action columns are capped and the final data column fills the remaining width; full values are retained with tooltips.
- Because (inferred from `specs/ui-language.md` compact-table refinement): centered columns made long names and editors jump; density and readability improved with left alignment.
- Consequences: the `CompactDataGrid*` styles; utility checkboxes and action icons stay centered; text is never shortened in the model.

## DEC-013 — Actionable validation

- Status: accepted · 2026-10-02 · Scope: validation
- Decision: validation shows a concise cause and remedy in the severity-coloured frame with an action button on its right that navigates to the right screen, reveals and focuses the exact field, and preserves drafts; it stays until revalidation succeeds.
- Because (inferred from commit "Document actionable validation navigation convention"): a message that names a problem without a way to reach the fix is a dead end.
- Consequences: `ValidationWarningFrame` and `ValidationWarningAction`; conditions with no in-app remedy explain the next step instead of showing a button.

## DEC-014 — Pixel-perfect from approved mockups; a mockup is not approval

- Status: accepted · 2026-09-21 · Scope: process
- Decision: an approved image is implemented to match pane proportions, spacing, typography, colours and states exactly; a direction or variant approval is not build approval; new proposals are made by capturing the running app and editing only the changed area.
- Because (inferred from `specs/message-library/design/README.md`): "similar" implementations drifted from what was approved, and mockups recreated chrome that already existed.
- Consequences: proof captures are compared side by side at 100 % scaling; generated images do not override production tokens.

## DEC-015 — Vector icons only

- Status: accepted · 2026-09-12 · Scope: icons
- Decision: action icons are named vector paths in one family, 16 DIP optical box and 1.5 DIP strokes; no Unicode glyph substitutes.
- Because (inferred from the ui-language audit): font glyphs rendered inconsistently and did not match the Watch, Refresh and Copy paths.
- Consequences: `IconResources.xaml`; glyph dimensions are not hit targets.

## DEC-016 — Settings without a duplicate connection picker

- Status: accepted · 2026-09-12 · Scope: settings
- Decision: General settings omit the connection picker already in the toolbar; automatically connecting when switching profiles is an opt-in saved switch that does not bypass warnings.
- Because (inferred from commit "Simplify settings and add opt-in connection on profile switch"): two pickers for one state confused which was authoritative.
- Consequences: Add connection opens an empty editor without changing the active connection.

## DEC-017 — Compact icon-plus-label action strips

- Status: accepted · 2026-07-08 · Scope: actions
- Decision: dense action strips use compact icon-plus-label buttons with full command names in tooltips and stable automation names.
- Because (inferred from `Lessons/`): scannable for the user, testable for FlaUI.
- Consequences: the visible label explains the operation; automation names do not change with wording tweaks.

## DEC-018 — Hide inapplicable commands, explain blocked ones

- Status: accepted · 2026-07-08 · Scope: commands
- Decision: commands that do not belong to the selected entity context are hidden; commands that apply but are currently blocked stay visible, disabled, with a tooltip saying why.
- Because (inferred from `Lessons/`): "does not apply here" and "blocked right now" are different answers and the user needs to tell them apart.
- Consequences: disabled state is a first-class state in every button style.

## DEC-019 — Pinned package versions

- Status: accepted · 2026-07-22 · Scope: build
- Decision: NuGet package versions are pinned.
- Because (inferred from AGENTS.md): a quality review found floating versions risky for reproducible releases.
- Consequences: no floating ranges in any csproj.

## DEC-020 — Documentation boundaries

- Status: accepted · 2026-07-22 · Scope: docs
- Decision: README is user-facing; contributor setup lives in CONTRIBUTING.md and tests/README.md; maintainer release mechanics and agent guidance live in AGENTS.md.
- Because (inferred from AGENTS.md): tagging and publishing procedures must not read as contributor instructions.
- Consequences: a behaviour change updates the narrowest authoritative document only.

## DEC-021 — No numeric navigation badges, native title bar

- Status: accepted · 2026-09-12 · Scope: layout
- Decision: no counter badges in navigation, no duplicate app banner; the native title bar carries the app name and icon; Settings uses the native title without a content heading.
- Because (inferred from `specs/ui-language.md`): duplicated headings and badges added noise without information.
- Consequences: attention signals use colour and labels in place, not counts on tabs.

## DEC-022 ? Responsive Workbench editor

- Status: accepted ? 2026-10-02 ? Scope: Workbench layout
- Decision: show JSON beside properties when usable editor width permits; otherwise use Body/Properties/Variables tabs. Medium layouts use two property columns; small layouts use one. New windows default to 1200x800 while retaining saved sizes and the 980x640 minimum.
- Because: the user wanted a combination of proposals 2 and 3: "if we have a lot of space there is no need to hide the Json body editor", and clarified "if window will be smaller then we should have single column."
- Consequences: resize the existing controls without losing drafts, selection or editing context. Keep Variables and advanced controls reachable. See the [approved contract](specs/message-library/responsive-layout.md).

## DEC-023 — Workbench search context and visible inspector choices

- Status: accepted · 2026-10-02 · Scope: Workbench search and navigation
- Decision: extend entity suggestions to Workbench namespace search and offer template/folder suggestions with folder context. In wide Compose, show adjacent Properties and Variables tabs using shared selected-tab styling, with JSON always visible.
- Because: the user wanted more context about search possibilities and found the distant Variables link misleading and hard to discover.
- Consequences: suggestion actions are specific to Workbench filtering or library selection; they do not retarget a draft destination or start broker message searches. Preserve narrow-layout tabs and responsive editing state. See the [approved contract](specs/message-library/search-and-inspector-tabs.md).

## DEC-024 — Styled scheduling inputs with constrained time selection

- Status: accepted · 2026-10-02 · Scope: Workbench scheduling
- Decision: use an app-styled calendar and noneditable hour/minute selectors, with vertically aligned radio labels.
- Because: the user found the native date picker visually inconsistent, free-text time error-prone, and radio labels misaligned.
- Consequences: preserve UTC/Local and future-time validation; no additional time zones or broker behavior. See the [approved contract](specs/message-library/scheduling-controls.md).

## DEC-025 — Compact, vertically centered date field

- Status: accepted · 2026-10-03 · Scope: Workbench scheduling
- Decision: match the date field to the time controls below; center the date text vertically while retaining left alignment.
- Because: the user found the date selector "too long" and its text "too low."
- Consequences: reuse the existing scheduling components and preserve calendar and scheduling behavior. See the [scheduling contract](specs/message-library/scheduling-controls.md).

## DEC-026 — Calendar-only, content-sized date field

- Status: accepted · 2026-10-03 · Scope: Workbench scheduling
- Decision: fit the field to the displayed date and calendar button with a small gap; clicking the date opens the calendar and manual date entry is disabled.
- Because: the user still found the field too wide and wants dates chosen through the popup.
- Consequences: supersedes DEC-025 width matching while retaining vertical centering; preserve keyboard calendar access and scheduling validation. See the [scheduling contract](specs/message-library/scheduling-controls.md).

## DEC-027 — Distinct Watch and auto-refresh indicators

- Status: accepted · 2026-10-03 · Scope: namespace tree
- Decision: underline watched Active/DLQ counts on queues and subscriptions; show a refresh glyph beside the current Investigation entity and a muted pause glyph when explicitly paused.
- Because: the user wants a subtle indication of watched buckets and automatic refresh without confusing the two independent features; approved revised proposal 25.
- Consequences: reflect effective inherited Watch rules and exclusions, preserve count colors/alignment and symbols, and explain states in tooltips and the legend. Auto-refresh follows the current browse view and remains a window-wide interval; this does not introduce per-entity refresh settings. Scheduled and topic aggregate counts have no Watch underline. See the [approved mockup](specs/message-library/design/proposed/25-watch-and-auto-refresh-indicators.png) and [design registry](DESIGN.md).

## DEC-028 ? Background message refresh for watched scopes

- Status: accepted ? 2026-10-03 ? Scope: Watch and automatic refresh
- Decision: refresh watched topic/subscription message views in the background using one shared interval; retain visible indicators after changing selection or workspace.
- Because: the user wants both arrival monitoring and refreshed message lists for watched locations while browsing elsewhere, and chose one shared interval.
- Consequences: supersedes DEC-027's current-view-only refresh scope. Reuse its existing glyphs, underlines, colors and legend. Topic Watch expands to subscriptions and respects bucket choices/exclusions. Cache one page or previously loaded depth using non-consuming peeks, clear caches on disconnect/profile change, and retain last successful results on refresh failure. Pause/Off stop refresh but leave Watch notifications independent. The current un-watched Investigation view continues its existing automatic refresh behavior.

## DEC-029 ? App-wide date display preference

- Status: accepted ? 2026-10-03 ? Scope: displayed dates
- Decision: Settings offers Follow Windows (default), yyyy-MM-dd, dd/MM/yyyy, MM/dd/yyyy and dd MMM yyyy; month names use the Windows language. Apply the choice throughout the app and remove the lingering date-text highlight after calendar selection.
- Because: the user wants Windows conventions by default and alternatives for individual preferences.
- Consequences: formatting never changes instants, UTC/Local selection, payload JSON or machine exports. See the [approved Settings proposal](specs/message-library/design/approved/26-date-format-settings.png) and [date-display contract](specs/date-display.md).

## DEC-030 - Simplified inspector tabs and actions

- Status: accepted, 2026-10-03, Scope: Investigation inspector
- Decision: name the content tab Body, remove Raw, align Create template and Replay to the right, and add the existing Add icon to Create template.
- Because: the user finds the left-clustered actions odd and the Raw view redundant beside the formatted body.
- Consequences: preserve body formatting, text/binary fallback, Properties, editing, Copy and Find. Keep the original payload available internally for template capture. At narrow inspector widths actions flow to a right-aligned second row without overlapping tabs.

## DEC-031 - Inspector refinement and explicit replay history

- Status: accepted, 2026-10-03, Scope: Investigation inspector and replay
- Decision: preserve the full inspector metadata/actions while adding JSON folding, Find above the body, Modified inside the Body tab's shared background/underline, and hover/focus Copy on the first scrolling document line. Replay reviews an editable generated outgoing ID and the chosen body, without property editors. A separate modeless history window keeps per-attempt records and filters Investigation through Find all replays or a per-row Find replay action.
- Because: the user needs readable nested payloads, a clear exact outgoing identity, and a history window that can stay open while investigating copies.
- Consequences: original DLQ messages remain until explicit deletion. Absence is labelled as a last complete Active/DLQ observation, never confirmed API processing. Incomplete observations cannot qualify for history cleanup. [Replay contract](specs/replay-history.md) preserves the implemented identity, persistence, observation and deletion safeguards.
- Follow-up approved 2026-10-05: reopen history through a compact Investigation toolbar action; retain attempts until their connection profile is removed. No separate clear-history action is in scope.

## DEC-032 � Replay identity and themed folding

- Status: accepted, 2026-10-05, Scope: Investigation presentation.
- Decision: place the outlined history-clock Replay history action immediately left of Settings. Mark exact saved replay IDs with a compact Replay badge, independently of Active/DLQ, and keep delivery state visible when source paths are long. Use app-themed chevrons and quiet dark collapsed placeholders in the shared JSON editor.
- Because: the play icon was misleading, replay provenance was invisible, long paths hid delivery-state badges, and default folding controls looked inconsistent with the application.
- Consequences: labels use saved history scoped to the current profile and namespace, excluding not-sent attempts; uncertain sends remain explicit in the tooltip. Preserve editor content, undo, search expansion and keyboard behavior. Approved references: Replay 35 and Inspect 36 in [UI language](specs/ui-language.md).

## DEC-033 - Location hierarchy and compact message table

- Status: accepted, 2026-10-05, Scope: Investigation browse/search table
- Decision: implement Location 37 option A with existing topic/subscription icons, muted topic above an indented darker subscription, independent Active/DLQ badges, and a one-line queue fallback. Remove the Correlation ID column and its row Copy action; retain both in the inspector. Distinguish replay IDs from per-subscription deliveries in replay-search summaries.
- Because: long topic prefixes hid subscription names, making fan-out deliveries look duplicated. Correlation is already available when inspecting a message; removing its column gives location more room without taller rows.
- Consequences: preserve exact-ID search, replay behavior, row selection, source tooltips and incomplete-scan status. Location 37 changes presentation only.
## DEC-034 - Correlation column follows view scope

- Status: accepted, 2026-10-05, Scope: Investigation message table
- Decision: show Correlation ID and its existing Copy action for a single queue or subscription, in both Active and Dead letter. Hide it for search and topic-wide views, where Location / State needs the space. Keep correlation and Copy in the inspector throughout.
- Because: removing correlation everywhere left single-entity lists sparse, while multi-location results need room to distinguish subscriptions.
- Consequences: supersedes DEC-033's unconditional correlation-column removal only. Preserve Location 37's hierarchy, icons, state badges, replay summaries and fixed side-by-side Investigation panes.

## DEC-035 - Shared message column selection

- Status: accepted, 2026-10-05, Scope: single-entity Investigation tables.
- Decision: Columns 38 exposes default, system and observed application properties through an immediate checkbox popup. One global selection is shared across queues, subscriptions, Active/DLQ and profiles, and saved across restarts.
- Because: the user wants to choose the useful message properties while retaining the current columns as defaults.
- Consequences: selection checkboxes remain; at least one data column stays enabled. Saved application keys remain selected when absent, displaying a dash. Search/topic-wide location layouts remain unchanged. This extends DEC-034 with explicit user choices, without changing broker actions.

## DEC-036 - Visible direct-editing affordance

- Status: accepted, 2026-10-05, Scope: Investigation inspector.
- Decision: Inspect 40 adds a small pencil + Editable or lock + Read-only label at the lower-right inside the dark editor, with reserved space and accessible help. DLQ body editing remains immediate; Properties, Active bodies and pending replay remain read-only.
- Because: the user likes direct typing but rejected editing as "secret knowledge" and disliked both Inspect 39's mode gate and prominent explanation.
- Consequences: retain Modified, Discard, folding, Find, hover Copy and replay behavior. No extra click is required to edit. Approved images and interaction contract remain in [UI language](specs/ui-language.md).

## DEC-037 - Copy follows the visible inspector viewport

- Status: accepted, 2026-10-05, Scope: Investigation Body and Properties.
- Decision: keep the existing hover/focus Copy button at the top-right of the visible document viewport while scrolling. Focusing Copy must preserve the scroll position. Reserve its action lane in both views so it cannot cover text.
- Because: the user found Copy disappearing when scrolling down and expects it to follow the visible content.
- Consequences: supersedes only DEC-031/Inspect 33's first-line scroll-away behavior. Preserve styling, hover/focus visibility, copied content, editor state and all other inspector/replay interactions.

## DEC-038 - Visible message state and shared reorderable columns

Status: accepted, 2026-10-05.

The user approved Columns 41 and selector Move up / Move down actions because DLQ state must be obvious in every list and enabled properties should be arranged as desired. Pin selection and mandatory State at the start of every grid. Move the existing badge out of Location / State and rename that column Location. Single-entity lists support header dragging and selector moves; both persist one shared order across restarts, independently of visibility. Hiding/re-enabling retains position, first-time fields append, and reset restores both defaults. Topic/search data order stays fixed. This extends DEC-035 without changing broker behavior.

## DEC-039 - Order-based column sizing and viewport editor overlays

Status: accepted, 2026-10-05.

The user reported an oversized middle Enqueued column after dragging and requested sizes that follow visible order, not property identity. Compact leading data columns use content sizing with a shared responsive cap; the last visible data column fills remaining space. Recalculate after reorder, visibility changes and resize. Selection/State remain fixed. Investigation cell backgrounds inherit the row so hover/selection never paints isolated patches.

Copy and the persistent Editable/Read-only indicator overlay the code viewport with a 24 DIP right inset, leaving a gap beyond the 18 DIP scrollbar. Status has no separate row; both surfaces reserve a 128 DIP right text lane so overlays do not obscure content. Copy retains hover/focus behavior; status retains its semantic visibility and help. This refines DEC-036/037/038 without changing editing or broker semantics. Record and test these invariants to prevent regressions.


## DEC-040 - Delete only explicitly checked messages

- Status: accepted 2026-10-05.
- Decision: Investigation Delete targets only checked rows. With zero checked rows it is disabled, even when a message is focused in the inspector. Replay retains its existing focus behavior.
- Because: the user saw 0 selected while Delete offered to delete one focused message; the counter and destructive action must agree.
- Consequences: remove the focused-row fallback. Preserve the captured-target confirmation, exact DELETE gate, Active/mixed-selection safety and cancellation behavior. This supersedes the focused-delete fallback in DEC-002 and the Investigation handoff.


## DEC-041 - Readable scope and persistent selection

- Status: accepted, 2026-10-05.
- Decision: implement Location 44 #1-4: hierarchical topic/subscription header, All subscriptions for topic scope, and persistent sidebar browse-scope highlighting independent of inspected rows.
- Because: the flat header was unreadable and the user could not see which scope was selected.
- Consequences: preserve search identity and navigation semantics. See [Location 44](specs/ui-language.md). Approval is not implementation proof.

## DEC-042 - Compact columns and empty remainder

- Status: accepted, 2026-10-05.
- Decision: implement Replay 45 #4-5 for Investigation message grids: content-sized real columns within responsive caps, adjacent correlation Copy, and an empty trailing remainder.
- Because: stretching the last real column separates its value from its action and wastes readable space.
- Consequences: supersedes only DEC-012/DEC-039 final-column fill for Investigation message grids; preserve ordering, visibility, pinned columns and editor overlays. See [Replay 45](specs/ui-language.md).

## DEC-043 - Replay history identities and row removal

- Status: accepted, 2026-10-05; Replay 45 v2 explicitly approved.
- Decision: Original message ID with search first, Replay ID with search second, followed by replay details/state. Place local-history trash at the row end when a complete status check reports Not found.
- Because: the selected-details paragraph is unreadable and deletion should be adjacent to its target.
- Consequences: replace Replay 45 v1's unconditional selected-panel removal proposal; do not confuse local history removal with Delete original. Propose concise labelled selected details. See [Replay 45](specs/ui-language.md).

## DEC-044 - Replay 47 inline history details

- Status: accepted, 2026-10-05; user explicitly chose Replay 47 (Opus) and requested implementation.
- Decision: use one table with state counts and selected-row inline details, full IDs and Copy, source/destination and original deletion beside the original. Retain row-local eligible history trash.
- Because: the permanent selected panel repeated dense information and created a cramped inner scrollbar; the user preferred Opus's inline alternative to the stable-row proposal.
- Consequences: supersedes only DEC-043 selected-details layout. Preserve modeless searches, truthful observations, existing cleanup safeguards, storage and broker operations. See [Replay history contract](specs/replay-history.md).

### DEC-044 visual correction

The user rejected the first implementation because it did not visually follow Replay 47. The approved image is a visual contract, not only a structural guide: use prominent filled state badges, readable typography, Copy beside each full ID, and a compact horizontal source row. Remove the redundant Destination row while retaining its information in a tooltip. Existing replay safety behavior stays unchanged.

DLQ palette clarification: the user explicitly disallowed changing DLQ tag colors. Preserve DeadLetterBrush and DeadLetterTextBrush for original-source and observed-copy DLQ tags, regardless of generated mockup coloring.
