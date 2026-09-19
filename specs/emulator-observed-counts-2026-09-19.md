# Emulator observed counts and first-launch connection

## Agreed behavior

The emulator profile uses fetched deliveries as a navigation-count workaround. Azure profiles retain broker counts. A fresh preferences store selects the built-in Local emulator profile and attempts connection automatically; existing saved startup choices and corrupt-file recovery behavior are preserved.

Counts marked `*` are browsing observations, not live totals. Unvisited buckets show `—`; scheduled totals stay unavailable. The tooltip gives the observation time in UTC and partial/complete scan status. Main-queue peeks may include scheduled, deferred and expired messages. Topics count subscription deliveries, including separate copies with identical message IDs or sequence numbers.

Refresh replaces the observation, Load more extends it, and confirmed deletion adjusts cached observations. Retained rows absent from the latest peek do not contribute. Reobserving such a row clears its stale marker. Reconnect discards the cache. Removed entities and incomplete discovery invalidate affected observations; an empty topic retains its successful zero observation across unchanged discovery.

## Verification

Local evidence is under `artifacts/emulator-observed/results` (ignored build artifacts):

| Evidence | Result |
| --- | --- |
| Initial regression tests, `observed-red.trx` | 9 failed / 14 before implementation |
| Retained-row and removed-subscription regressions, `edge-red.trx` | Both failed before repair |
| Empty-topic refresh regression, `empty-topic-red.trx` | Failed before repair |
| Full core suite, `core-full.trx` | 188 passed |
| App suite before final empty-topic repair, `app-full.trx` | 311 passed |
| Final app suite, `app-final.trx` | 311 passed; one existing profile-migration test failed in sandbox |
| Same profile-migration case under Windows user context, `preferences-desktop.trx` | Passed; no code change needed |
| Real emulator/workspace/UI workflows, `ui-workflows.trx` | 57 passed |
| Final affected browse/window rerun, `ui-final.trx` | 23 passed after the empty-topic repair |

Twenty new test cases cover counts, startup, and UI paging; existing UI assertions were strengthened. The real-window paging test observes 50 deliveries, loads 65, completes one externally, then refreshes to 64 while retaining the focused missing row for inspection. The tests use an isolated emulator on ports 5674/5302 and synthetic data.

## UI verification

The approved direction and existing WPF components were the design authority. Six screenshots of active and DLQ counts were inspected at 1500×1000, 1100×800, and 980×640. They are in `artifacts/emulator-observed/build/bin/ServiceBusEmulatorExplorer.UiSmoke.Tests/debug/daily-fix-captures`.

| Gate | Result | Evidence |
| --- | --- | --- |
| Design authority/readiness | PASS | User-approved emulator workaround and startup choice; existing styling retained |
| Rendered flow/states | PASS | Real WPF refresh, pagination, external arrival/removal, active/DLQ, auto-refresh, reopen, and inspector workflows |
| Geometry/content | PASS | Counts and summaries readable at all three tested sizes; partial multi-page data exercised |
| Accessibility/responsiveness | PARTIAL | UI Automation names and Invoke/Toggle paths exercised; no full keyboard/screen-reader certification. Previously known external RichTextBox text-provider limitation remains |
| Clean close/regression | PASS | Final 23 affected browse/window cases passed; no production debug UI or screenshot replacement |

Independent agent review could not start because the session's agent thread limit was reached. Primary-agent diff review and regression evidence were used; an independent review is not claimed. Live Azure execution was not available; cloud count isolation is covered with tests. Observations are session-local snapshots, not atomic or continuously updated broker totals.
