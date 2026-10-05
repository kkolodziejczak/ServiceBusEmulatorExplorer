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

Existing filenames remain stable for retained references. Superseded draft boards, rejected
alternatives and completed execution ledgers are not part of the current specification.
Keep future proposals here only while they are being considered; once implemented, retain
the useful final reference and move binding rules into DESIGN.md and DECISIONS.md.
Unfinished feature plans elsewhere in specs remain in force.
