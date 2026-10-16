# UI reference index

[DESIGN.md](../DESIGN.md) owns current visual rules and registered components;
[DECISIONS.md](../DECISIONS.md) owns accepted behavior and its rationale;
[CAPABILITIES.md](../CAPABILITIES.md) points to reusable implementations.
Read those documents before changing the UI. Generated boards govern approved composition;
existing application tokens govern exact colors, icons and typography. In particular, preserve
the existing pale-orange/dark-orange DLQ palette.

## Current approved references

| Feature | Final reference and scope |
| --- | --- |
| Investigation panes | [Side-by-side layout](investigation-side-by-side-approved.png) |
| Workbench search and inspector tabs | [Contract](message-library/search-and-inspector-tabs.md) |
| Scheduling | [Contract](message-library/scheduling-controls.md) |
| Date formats | [Contract](date-display.md) |
| Namespace filtering and initial workspace | [Board 21](message-library/design/proposed/21-namespace-and-default-workspace.png) |
| Validation, results and activity log | [Board 22](message-library/design/proposed/22-validation-results-and-activity-log.png) |
| Mapping dialog sizing | [Board 23](message-library/design/proposed/23-content-sized-mapping-dialog.png) |
| Watch and refresh indicators | [Board 25](message-library/design/proposed/25-watch-and-auto-refresh-indicators.png); DEC-028 controls background scope |
| Replay review form | [Board 29](message-library/design/proposed/29-replay-results-and-history.png), review form only; current history is Replay 47 |
| Modified Body tab | [Inspect 33 v2](message-library/design/proposed/INSPECT-033-r2-body-tab.png); Copy remains fixed in the document viewport per DEC-037 |
| Replay and delivery badges | [Replay 35](message-library/design/proposed/Replay-35-v1-result-badges.png) |
| JSON folding | [Inspect 36](message-library/design/proposed/Inspect-36-v1-json-folding.png) |
| Delivery location hierarchy | [Location 37 v2](message-library/design/proposed/Location-37-v2-delivery-hierarchy.png) |
| Column selector | [Columns 38](message-library/design/proposed/columns-38-v1.png), option 3 |
| Editor status | [Inspect 40](message-library/design/proposed/inspect-40-v1-subtle-editability.png); overlay placement follows DESIGN.md |
| State column and saved ordering | [Columns 41](message-library/design/proposed/columns-41-v1-state-and-order.png) |
| Scope header and sidebar selection | [Location 44](message-library/design/proposed/location-44-v1-scope-header.png) |
| Compact columns and empty remainder | [Replay 45](message-library/design/proposed/replay-45-v1-history-cleanup-and-columns.png), callouts 4 and 5 only; its history proposal is not authoritative |
| Replay history | [Replay 47](message-library/design/proposed/replay-47-v1-opus-inline-details.png) and [current contract](replay-history.md) |
| Generated values | [Prepare 64](message-library/design/proposed/prepare-64-v1-generated-readonly.png), DEC-045 |
| Schedule validation | [Schedule 66](message-library/design/proposed/schedule-66-v1-past-time-validation.png), DEC-046; the notice uses the existing amber validation style, not the red drawn on the board |

## Proposals under consideration

| Proposal | Question |
| --- | --- |
| [Delimiter 67 v1](message-library/design/proposed/delimiter-67-v1-options.png) | Lighter delimiter labels for the Map CSV dialog: A key-cap chip, B character first, C shortcut-style column, D inline dot. Replaces Delimiter 65 "Comma (,)", rejected as too heavy. Awaiting choice. |
| [Refresh 48 v1](message-library/design/proposed/refresh-48-v1-auto-refresh-label.png) | Label-only options A–D for the tab-row auto-refresh control (commit d5db402, made without a proposal). All rejected; superseded by Refresh 49–53. |
| [Refresh 49 v1](message-library/design/proposed/refresh-49-v1-footer.png) | Option 1: auto-refresh caption, interval and pause in the window footer beside UTC/Local. |
| [Refresh 50 v1](message-library/design/proposed/refresh-50-v1-refresh-group.png) | Option 2: Refresh joins interval and pause in the tab row; group wraps under the tabs when narrow. |
| [Refresh 51 v1](message-library/design/proposed/refresh-51-v1-stacked-caption.png) | Option 3: "Auto-refresh" caption stacked above the tab-row control. |
| [Refresh 52 v1](message-library/design/proposed/refresh-52-v1-split-button.png) | Option 4: Refresh split button carrying interval and pause. |
| [Refresh 53 v1](message-library/design/proposed/refresh-53-v1-combined-footer.png) | Option 5: footer group plus the existing list-heading refresh marker; Pause disabled when Off. |
| Refresh 49–53 | All rejected 2026-10-07: they moved the control instead of rethinking the feature. Superseded by Updates 54–58. |
| [Updates 54 v1](message-library/design/proposed/updates-54-v1-live-alerts.png) | Live + Alerts (claude-fable-5-1): one Live toggle, Alerts replace Watch, live search with "Alert me". |
| [Updates 55 v1](message-library/design/proposed/updates-55-v1-follow-and-find.png) | Follow and Find (gpt-6.1-sol): per-view Follow, Updates task list, Wait for matches. |
| [Updates 56 v1](message-library/design/proposed/updates-56-v1-fresh-view-explicit-waits.png) | Fresh view, explicit waits (gpt-6-astra): Updates menu, keep this view fresh, Wait for a message form. |
| [Updates 57 v1](message-library/design/proposed/updates-57-v1-live-arrivals-feed.png) | Live mode + Arrivals feed (claude-opus-5-5): namespace-wide arrivals list with "Landed in" and notify on match. |
| [Updates 58 v1](message-library/design/proposed/updates-58-v1-combined-live-and-waits.png) | Combined: Live pill with a panel listing everything kept up to date, per-view freshness line, Alert per place, search that keeps searching. Awaiting choice among 54–58. |

Existing filenames remain stable for retained references. Superseded draft boards, rejected
alternatives and completed execution ledgers are not part of the current specification.
Keep future proposals here only while they are being considered; once implemented, retain
the useful final reference and move binding rules into DESIGN.md and DECISIONS.md.
Unfinished feature plans elsewhere in specs remain in force.
