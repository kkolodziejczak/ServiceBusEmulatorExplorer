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

## DEC-001 — Replay is non-destructive by default

- Status: accepted · 2026-07-08 · Scope: DLQ
- Decision: replaying a dead-lettered message creates a new active message and leaves the original in the DLQ until a separate, confirmed delete.
- Because (inferred from `Lessons/`): an explorer tool must never lose the evidence of a failure while trying to recover from it.
- Consequences: replay and delete are different commands and different services; a replay whose DLQ-lock abandon fails is still reported as a successful send.

## DEC-002 — Delete is separate, visibly destructive and scoped

- Status: accepted · 2026-09-12 · Scope: DLQ
- Decision: delete is a red outline action with exact typed `DELETE` confirmation; multi-delete and visible-page delete confirm explicitly; production delete is restricted to captured DLQ deliveries, and mixed Active selections disable it.
- Because (inferred from `specs/ui-language.md` and AGENTS.md): deletion is irreversible and the emulator can mis-report counts, so only messages the tool has actually seen may be targeted.
- Consequences: `DestructiveButton` style; checked messages form the batch, otherwise the focused message; cancellation after settlement still reports confirmed per-delivery outcomes.

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
