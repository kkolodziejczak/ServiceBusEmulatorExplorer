# Promote the investigation prototype

Prepared 2026-09-11; prototype approved by the user on 2026-09-12. **Prototype complete; ready for production implementation after the relevant broker questions below are answered.** This handoff does not authorize a release. No planning skills were used.

## Starting point

- Implementation baseline: `8575a2c` on `prototype/investigation-workspace` (approved prototype). Earlier milestones: `a908ffd` for persisted Watch/preferences and typed deletion; `8cc1232` for profile themes, warnings and new profiles. Use the normal checkout and inspect newer commits before starting; preserve unrelated changes.
- Visual/interaction reference: [prototype](../tools/ServiceBusEmulatorExplorer.InvestigationPrototype/README.md) and its [current preview](../tools/ServiceBusEmulatorExplorer.InvestigationPrototype/preview.png). Apply [UI language and audit corrections](ui-language.md) consistently; preserve the approved layout.
- Destination: existing WPF [App](../src/ServiceBusEmulatorExplorer.App/ServiceBusEmulatorExplorer.App.csproj) using existing [Core contracts](../src/ServiceBusEmulatorExplorer.Core/ServiceBus/ServiceBusContracts.cs). Keep the prototype as a reference until parity is proven. Do not just rename its executable or ship its synthetic Workspace.
- Current evidence: 347 routed walkthrough checks and an 11-check real tray run pass, including profile/Watch preferences, typed deletion and shared styling. Full-profile themes, saved warnings, new profiles and simplified Watch search pass the current rendered walkthrough. No new broker integration tests were run for this audit. The proof uses synthetic messages and is not a production readiness certificate.

## Approved prototype milestone

- [x] User approved the prototype as ready; the layout and implemented interactions are the design reference.
- [x] Final toolbar approved: no accent line; retain only the neutral bottom divider. Conventional Settings cog, no duplicate Settings heading, shared rounded copy icons beside masked connection fields, and themed scrollbars are part of the final design.
- [x] Shared styling, complete profile themes, clear-red Delete, compact layouts and simplified Watch search implemented.
- [x] Toolbar connection selector, profile creation/editing, optional warnings and off-by-default auto-connect-on-switch implemented. The duplicate General-settings connection picker is removed.
- [x] Preferences persist, including per-profile Watch inclusions and connection warnings; credentials use Windows user-scoped protection.
- [x] Active/DLQ deletion requires exact `DELETE`; warning cancellation preserves the approved connection and pending saves cannot select an unapproved profile.
- [x] Rendered walkthrough, restart/persistence and tray evidence recorded in the [prototype report](../tools/ServiceBusEmulatorExplorer.InvestigationPrototype/verification.md) and [tray report](../tools/ServiceBusEmulatorExplorer.InvestigationPrototype/tray-verification.md).
- [ ] Production workflows implemented and proven against real brokers (separate completion checklist below).

Do not reopen visual design, add redundant controls, or run another prototype-design phase by default. Transfer the approved interaction contract to the existing App. Physical keyboard, screen-reader and DPI/multi-monitor validation still belongs to production verification; approval does not turn missing evidence into a pass.

## Requirements mapped to what exists

“Prototype” means the interaction exists, not that the broker behavior exists.

| Conversation requirement | Current state | Promotion work |
| --- | --- | --- |
| Small daily-investigation UI; remove redundant headings | Approved layout implemented | Replace old shell surface; do not bring back all old administration buttons |
| Emulator and Azure connection selection | Toolbar dropdown with opt-in auto-connect; Settings manages profiles; Add connection opens a blank uniquely named profile without switching; Save persists | Reuse real profile/auth validation; persist selected profile and color separately from connection health |
| Share connection strings | Copy beside each masked runtime/administration field copies its current value; empty fields disable Copy; status never includes credentials | Preserve explicit clipboard sharing and masking; never log credentials |
| Profile identity and connection health | Active profile theme covers buttons, selected states and surface tints across windows; separate fixed health, DLQ and red Delete colors | Bind health to structured connection/operation outcomes; color alone must not communicate status |
| Queues, topics, subscriptions and Message/DLQ totals | Sample tree, distinct icons; unknown sample counts | Bind real tree and typed known/unknown/stale count state; topic totals are delivery totals across subscriptions |
| First 50 on scope/bucket change; Load more | Implemented against fixtures | Per-source cursors, cancellation, stale-request rejection; settle topic aggregate paging |
| Independent checkboxes; aligned select-all | Implemented | Stable source-aware row identity; select-all means displayed results, not unseen broker messages |
| Refresh/auto-refresh/pause without losing work | UI implemented; refresh can inject samples | Read-only, single-flight refresh preserving focus, selection, drafts and scroll; no simulated arrivals |
| One left search with entity/ID suggestions | Implemented | Real entity index plus loaded/recent IDs; never imply suggestions enumerate unseen broker messages |
| Global correlation/message ID search, OR and * | Implemented over complete memory snapshot | Cancellable paged peeking across sources; bounded scan and truthful incomplete status |
| Exact case-sensitive IDs; OR keyword ignores case; quoted literals | Implemented, including explicit correlation:/message: clauses | Preserve grammar and tests; do not add ? or implicit substring ID matching without approval |
| Search tree shows only matching branches and match counts | Implemented; totals retained separately | Project discovered matches, aggregate parents, restore authoritative totals on Clear |
| Location/State in global search and topic views | Implemented; hidden when browsing one queue/subscription | Preserve source identity even when the column is hidden |
| Clear removes applied filter and text | Implemented | Cancel old scans and restore browse scope; old callbacks must not repopulate results |
| Preview JSON, Properties, Raw; always wrap | Implemented in that order | Preserve bytes/metadata; label malformed/non-JSON honestly; no wrap toggle |
| Inline formatted/colorized edit, undo, discard | DLQ editor implemented | Preserve drafts by delivery identity; validate before send; define connection-switch behavior |
| Replay by default; Edit and Replay after changing JSON | Implemented with synthetic copies | Real send, batch eligibility and per-item outcomes; original DLQ remains unless separately deleted |
| Default IDs original-replay-N | Session-only counter and synthetic collision check | Persist or otherwise define counter/uniqueness; preserve metadata; never promise global retry count |
| Delete Active and DLQ messages, focused or checked batch | Confirmation lists Active/DLQ counts; exact uppercase `DELETE` enables the action; Cancel is the default | Preserve the typed gate; implement source-aware completion with per-item results, vanished/locked-message handling and cancellation; no purge or implicit unseen-message delete |
| Copy icon consistent and near correlation | Selected rounded icon everywhere | Share vector resource; copy exact ID/body/properties, preserve action labels and tooltips |
| Watch Active/DLQ independently at connection, topic or leaf scope | Global rules include future entities; topic rules include future subscriptions; explicit bucket overrides and a searchable inclusion tree; 15s fake-arrival timer | Refresh discovery before resolving effective sources; preserve category/topic/child inclusion precedence; detect arrivals without consuming messages; baseline, dedupe, reconnect and DLQ cursor policy |
| Persistent desktop alert; Investigate restores app | Separate topmost WPF window, real tray | Keep until response; position on correct monitor; no transient-toast substitution |
| Investigate adds to current cases | Implemented OR union, dedupe, mixed fields, focuses newest case | Preserve applied criteria; cancel superseded scan safely; handle expired/unavailable message |
| Close-to-tray configurable; explicit Exit | Real prototype lifetime behavior; preferences stored locally | Integrate app shutdown and cancellation; production settings migration; no invisible orphan process |
| Full-width activity console; clear only expanded | Implemented with 100 in-memory entries | Structured outcomes, redacted sensitive values, bounded history; decide persistence |
| UTC/Local/Server selector | Display preference persisted; Server is sample −05:00 | Configure real server zone; retain original UTC instants and raw payloads |
| Restore selections/preferences across launches | Separate prototype JSON store; user-scoped DPAPI for connection strings; per-profile Watch snapshots | Integrate production settings safely; migration, failure recovery and restart behavior must be proven |
| Consistent colors/fonts/sizes/states | Shared resources, themed scrollbars, conventional cog, a neutral gray bottom toolbar divider and Settings without a duplicate content title implemented; rendered desktop/compact/minimum checks pass | Preserve approved tokens and complete the linked UI audit before production handoff |

Keep entity creation/edit/delete, purge, advanced rule administration, scheduling and unrelated legacy features outside the primary workflow. Retaining existing backend code is different from exposing its entire old UI. Standalone Send New Message and replay of Active messages were not settled; do not silently add them.

## Questions before implementation

Answer by Q number. Recommendations are proposals, not already approved decisions. Existing approved interaction details above do not need to be asked again.

| ID | Decision needed | Recommended starting choice |
| --- | --- | --- |
| Q2 | Should Messages mean Active or total including DLQ/scheduled? | Active and DLQ separately, explicit tooltip; preserve unavailable counts. Never substitute a loaded-page length. |
| Q3 | Search limits and completeness: how much work may one search do? | Both Active+DLQ in selected connection; configurable time/message budget with Stop/Continue and visible partial status. Agree actual limits before coding. |
| Q4 | Watch interval, baseline, reconnect and notification backlog? | No initial-backlog alerts; configurable polling; retain distinct pending cases until response. Agree interval, cap and restart policy; 15s is only the mock default. |
| Q5 | Accept subscription replay publishing to its parent topic, potentially reaching other subscriptions? | Make destination and routing consequence clear before sending. A subscription is not a direct send destination. |
| Q6 | Replay numbering across sessions/users and ambiguous send failures? | Keep readable replay suffix plus guaranteed unique send identity; define persistence/ID format and retry policy. Do not auto-retry an uncertain successful send. |
| Q7 | Production log/draft persistence and Azure CLI auth visibility? | Profiles, selected connection, preferences and Watch selections are approved to persist with Windows user-scoped secret protection. Decide whether logs/drafts persist and how Azure CLI auth appears; do not reopen the settled preference-storage decision. |
| Q8 | Switching connection with dirty drafts/active work? | Warn about unsaved edits, cancel old operations, isolate all connection caches. Never silently discard production drafts. |
| Q9 | What is “Server” timezone? | Explicit per-profile Windows time-zone ID with DST rules; do not infer broker timezone or ship fixed −05:00. |
| Q10 | Topic view pagination: 50 total or 50 per subscription? | 50 combined deliveries with source identity, matching the simple UI; per-source cursors required. |

Q1 (delete scope) and Q11 (layout/density) are settled: retain typed confirmation for Active and DLQ, and preserve the approved compact layout. Do not add automatic console collapsing during promotion. Auto-connect, profile warnings, persistence and Watch inclusion semantics are also settled; only their broker implementation needs design work.

Resolve questions in small groups before the dependent work. The recommendations in this document are not consent:

| Work to start | Questions needed first |
| --- | --- |
| Profile/settings and read-only browsing | Q2 count meaning; Q7 authentication and persisted data; Q8 switching with work in progress; Q9 configured time zone; Q10 topic pagination |
| Global search | Q3 scan budgets and completeness |
| Watch and desktop notifications | Q4 polling, baseline, backlog and restart policy |
| Replay | Q5 topic routing and Q6 IDs/retries |

Independent resource migration and read-only repository investigation may continue while answers are pending. Do not invent product answers or block every worker on a question affecting only one feature. Ask only genuinely unresolved choices; implementation details such as focused class names, cancellation plumbing and atomic file writes belong to the coordinator.

Settled scope: delete supports Active and DLQ, focused or checked messages, with explicit typed `DELETE` confirmation. Connection selection uses the toolbar dropdown; Settings retains profile management and an off-by-default, saved auto-connect-on-switch option. Warning approval remains required before automatic connection; a saved profile color themes buttons, selection and surfaces across windows while semantic health, DLQ and red Delete colors retain their meanings. Add connection creates an empty editable profile without switching. An optional saved warning requires confirmation before switching, Connect or restored startup connection attempts; Cancel preserves the old connection or blocks the pending attempt. Watch search uses an inline magnifier and concise placeholder. Watch supports global and topic rules, including subsequently discovered entities, with a searchable inclusion tree and mixed parent states. A branch choice applies to descendants, and more specific child choices override inherited inclusion. Profiles and user selections/preferences persist across launches; Watch rules belong to a profile. These interaction decisions do not settle real polling or broker mutation algorithms above.

## Reuse and the traps to avoid

- [Message service](../src/ServiceBusEmulatorExplorer.Core/ServiceBus/ServiceBusMessageService.cs) already peeks non-destructively with `take`, `fromSequenceNumber` and cancellation. [MessageInspectionViewModel](../src/ServiceBusEmulatorExplorer.App/ViewModels/MessageInspectionViewModel.cs) already contains 50-row paging and stale-result defenses, but its refresh/selection behavior differs from the prototype. Extract focused workflows rather than adding every feature to this large class.
- [Administration service](../src/ServiceBusEmulatorExplorer.Core/ServiceBus/ServiceBusAdministrationService.cs) and [tree builder](../src/ServiceBusEmulatorExplorer.Core/ServiceBus/EntityTreeBuilder.cs) are reusable. Counts currently use non-null numeric fields, so unknown cannot yet be represented correctly.
- Topic Active/DLQ counts are initially constructed as zero in the administration adapter, then `EntityTreeBuilder.AggregateTopicCounts` sums subscription counts before returning discovery. Preserve this existing aggregation; add explicit unavailable/partial states so failed or incomplete discovery cannot become an authoritative empty topic. This detail was rechecked during production intake on 2026-09-12.
- [Client factory](../src/ServiceBusEmulatorExplorer.Core/ServiceBus/DirectServiceBusClientFactory.cs) validates the administration connection during Connect but does not prove runtime connectivity. A green Connected label must not imply both paths are healthy solely because administration discovery succeeded. Define truthful readiness and non-destructive validation with the connection worker.
- Historical [emulator evidence](codebase-audit-remediation-plan.md): on 2026-07-10, 60 messages were sent and a 50-message page was peeked while runtime ActiveMessageCount remained zero for three minutes. This is a recorded limitation, not a fresh emulator retest. Do not block discovery/search on reported zero or wait indefinitely for the emulator to fix counts.
- [Replay service](../src/ServiceBusEmulatorExplorer.Core/ServiceBus/DeadLetterReplayService.cs) and [request factory](../src/ServiceBusEmulatorExplorer.Core/ServiceBus/DeadLetterReplayRequestFactory.cs) support safe copy and separate DLQ deletion. Subscription replay maps to parent topic. The prototype's direct fan-out into child fixture lists is not real routing.
- Existing replay uses the NewGuid ID policy and existing deletion supports DLQ only. They are useful foundations, not parity with readable replay suffixes or approved Active deletion. Implement those gaps deliberately after the relevant contracts and questions are settled.
- Define and test the sendable metadata to preserve: the current mapper copies body, content type, correlation/session IDs, subject and application properties, but does not preserve every property (for example ReplyTo, To, PartitionKey or TTL). Broker-generated metadata remains observational, not replay input.
- [Profile store](../src/ServiceBusEmulatorExplorer.Core/Connection/JsonConnectionProfileStore.cs) supports saved profiles and Azure CLI formats, but connection strings are serialized and saves use direct file creation. Masked fields are not encryption; general settings need safe atomic persistence and migration.
- Global scan results are observations over time, not a transactional namespace snapshot. Peeking must not receive/lock/complete messages. Messages can disappear while a user inspects them.
- A simple increasing DLQ sequence watermark can miss an older message entering DLQ later. Prove a baseline/rescan strategy against the actual emulator and Azure before declaring Watch reliable. Counts alone cannot detect arrivals when counts are unknown or unchanged.
- Use identity `(connection generation, entity address, bucket, sequence number)` for a delivery. MessageId and CorrelationId are not unique row keys. Group batch mutations by actual source and expose partial outcomes.

## Implementation sequence: small stages, many Luna High workers

Use `gpt-5.6-luna` with reasoning effort `high` for workers. The main agent coordinates and reviews. No scheduled task is created by this document.

1. **Resolve questions and freeze contracts.** Coordinator records answers here, confirms selected branch and clean baseline, and defines delivery identity, count availability, scan progress, watch observations and mutation outcomes. Use the committed reference states. Ask only questions required by the next slice; leave unrelated stages pending. No worker invents conflicting shared contracts.
2. **Promote one usable read-only slice.** Shared themed resources + shell, real Settings/connect with profile creation, protected persistence, optional warnings and opt-in auto-connect; tree, first page, inspector, selection and Clear. Preserve cancellation before profile commitment and restore browsing after an approved reconnect. Keep old services and their tests. Prove this slice before adding mutations.
3. **Parallel feature work after contracts exist.** Suggested ownership below; coordinator owns shared shell wiring/DI and integration. Workers do not all modify ShellViewModel/MainWindow.
4. **Integrate and prove each feature.** Browse → global search → Watch/tray → replay/delete. Test stale results, cancellation, connection changes and partial failures at every boundary. Remove all simulation from the shipping composition.
5. **Final visual and broker gates.** Normalize remaining UI states, run real emulator proof, then authorized Azure proof if available. Commit completed milestones. Do not merge, tag or publish as a side effect of finishing this handoff.

| Luna High worker | Bounded ownership | Evidence required |
| --- | --- | --- |
| A: UI resources | New resource dictionaries/vector assets; coordinator integrates windows | All windows share tokens; contrast/focus/compact screenshots |
| B: Connection/settings | Profile/settings services and their tests | Safe persistence/migration, add/edit profiles, warnings before commitment, opt-in auto-connect, validation, configured time zones, secret redaction |
| C: Browse/counts | Entity/count projection and paging workflow | Unknown counts, source cursors, first 50/load more, stale-session isolation |
| D: Search | Pure matcher and cancellable scan service | OR/*/mixed IDs, partial/empty/errors, per-source bounds; no broker mutations |
| E: Watch | Observation/dedupe scheduler; tray/notification workflow in separate files | Baseline/reconnect/old-sequence DLQ arrivals, single-flight polling, persistent alerts |
| F: Message actions | Replay ID policy, batch outcomes, approved delete workflow | Original preserved, correct destination, partial/uncertain outcomes, confirmations |

Run up to six implementation workers concurrently when dependencies allow; use remaining slots for independent review, never duplicate ownership. Workers must request the coordinator's lease before Docker, ports, tests with shared runtime, databases, or external services. The coordinator serializes those operations and integrates patches. Pure code reads and isolated unit work can overlap.

The coordinator maintains a short execution record in this document: accepted question answers, current slice, exact worker-owned paths, shared contract revisions, active runtime lease, checks/results, milestone commits and next unfinished step. Keep completed prototype status separate from production progress. Freeze shared contracts before dispatching dependent workers. Give each worker the concrete input/output contract, allowed paths, acceptance checks and exclusions. Workers return changes, evidence and remaining issues; they do not independently change shared shell wiring or operate user environments. Coordinate build outputs as shared state too; tests in the same checkout are not automatically isolated.

Review each integrated slice with an independent worker against this requirement matrix and the changed code. The coordinator resolves findings and reruns affected checks before marking the slice complete. Preserve existing useful broker services/tests while replacing the old UI composition; keep ShellViewModel focused on orchestration. Do not copy the prototype's large code-behind or synthetic data model wholesale into production.

Before enabling Active or DLQ Delete, prove that the adapter can target the selected source/sequence safely, including sessions, locks, vanished messages and cancellation. Do not complete unrelated deliveries while scanning for targets. Unsupported broker/entity capabilities must produce an explicit unavailable result, not a fake success or purge fallback. Typed confirmation authorizes only the displayed targets; it does not authorize unattended testing against user messages. Mutation proofs use isolated fixtures on an authorized test environment.

## Production completion checklist (not prototype status)

### Execution record — 2026-09-12

- Active implementation objective established without a token budget. Verified normal checkout on `prototype/investigation-workspace`, clean at `6c16e3d`; this later commit changes the handoff only. Approved code baseline remains `8575a2c`.
- Q2, Q7, Q8, Q9 and Q10 proposals submitted to the user together; **answers pending**, not approved. Q3–Q6 remain for their dependent stages.
- Independent work underway: resource migration, inspector presentation and settled search grammar. Broker and parity audits are read-only. No production slice is complete and no new verification pass is claimed.
- Exact ownership: `resources` owns App `Investigation/Resources/`; `inspector` owns App `Investigation/Inspection/` and `InvestigationInspectionTests.cs`; `search_grammar` owns Core `Investigation/MessageSearchQuery.cs` and `InvestigationMessageSearchQueryTests.cs`. Coordinator owns shared contracts, shell/DI, integration and this handoff. `broker_audit` and `parity_inventory` own no writable files. Workers use Luna High.
- Shared contract: `DeliveryIdentity` contains connection generation, actual receiving address, bucket and sequence number; `MessageDelivery` retains the original `ExplorerMessage`. IDs are not delivery keys.
- Runtime lease: coordinator exclusively ran builds/fast tests, now released. Prototype process 3540 was observed running and left intact. No Docker, broker mutation or production UI-test execution has occurred.
- Next dependent step: record user answers and freeze profile/count/paging contracts, then implement and verify the read-only production slice. Resource-only work may continue meanwhile.

#### Foundation milestone evidence

- Added reusable approved style/icon dictionaries (including distinct Global Watch and Discard vectors), the pinned prototype AvalonEdit version, focused inspection presentation/colorization, settled ID query grammar, and source-aware delivery contracts. Resources are deliberately not merged into the old shell; the actual investigation shell is still outstanding.
- Core now retains original broker `RawBody` bytes alongside decoded text. An SDK-model-factory test covers invalid UTF-8. Delivery construction checks sequence agreement; tests distinguish sources, buckets and connection generations. Raw-tab integration remains a first-slice requirement, not a completed UI claim.
- Final fast checks: Core **68 passed**, App **75 passed**, zero failed; App WPF build succeeds. Commands used project-level `dotnet test --no-restore -m:1 -nr:false -p:UseSharedCompilation=false --blame-hang-timeout 60s`. Cached App dependency restore succeeded with `NuGetAudit=false`; no vulnerability-audit pass is claimed.
- Initial solution execution spawned excessive MSBuild workers and held output open; only verification workers in the observed launch interval were stopped. Explicit single-worker execution resolved the infrastructure issue. Intermediate missing package restore, date-string assertion, missing test import and empty SDK fixture-body errors were corrected before the final passes.
- Independent foundation reviews: `broker_audit` reviewed contracts/grammar/inspection; `parity_inventory` reviewed resource equivalence and paths. Removed field-ignoring matcher overload and misleading deduplication test, preserved typed metadata, handled non-finite numbers, added token/metadata and delivery tests, retained original bytes, and added missing vector assets. Embedded malformed-quote diagnostics remain identical to the approved prototype; both forms reject input, so the suggested diagnostic redesign was declined. No integrated slice review pass is claimed.
- UI verification is **BLOCKED for production parity**: shipping shell integration and supported-state execution are outstanding. A live reference capture was occluded and excluded from evidence. Committed prototype screenshots remain design authority, not production proof. Emulator, Azure, physical keyboard, screen-reader and DPI/multi-monitor evidence remain uncollected.
- No user answers have arrived for Q2/Q7/Q8/Q9/Q10. Dependent profile/settings/count/paging implementation remains pending those decisions. Q3–Q6 remain unasked for later stages. Foundation changes are ready for a scoped milestone commit; production completion checklist remains open.

- [ ] All questions affecting the current stage answered and recorded; unresolved stages remain unstarted.
- [ ] Approved interaction matrix works in the actual App, not just in the prototype executable.
- [ ] No synthetic fixture injection, fixed server offset, fake connection success or direct subscription fan-out in shipping paths.
- [ ] Fast existing unit/view-model tests remain green; meaningful new tests cover each changed workflow.
- [ ] Emulator proof includes >50 messages, multiple sources, Active+DLQ, zero/unknown runtime counts, cancellation, disconnect/reconnect, replay-copy and confirmed delete if approved.
- [ ] Azure behavior tested when credentials/environment are authorized; otherwise explicitly unverified. Never present emulator-only evidence as Azure certification.
- [ ] Rendered desktop/compact/settings/notification states and keyboard/DPI checks satisfy [UI language](ui-language.md); no user-as-first-tester handoff.
- [ ] Root README remains user-facing; narrow internal docs and affected links updated; `git diff --check` passes.
- [ ] Evidence and remaining limitations recorded; completed work committed on current branch; no release performed.

Start with existing commands in [tests README](../tests/README.md) and inspect scripts before running. Normal fast check: `dotnet test ServiceBusEmulatorExplorer.slnx --filter "TestCategory!=Integration&TestCategory!=UiSmoke"`. Integration/UI runs are opt-in and require the coordinator's runtime lease. Use bounded execution and the repository's UI smoke runner.

## Ready-to-use coordinator prompt

```text
Implement the approved investigation workspace in the real ServiceBusEmulatorExplorer WPF application. Act as the coordinator and use multiple gpt-5.6-luna subagents at high reasoning effort as workers.

Read AGENTS.md, specs/investigation-workspace-handoff.md, specs/ui-language.md and the prototype README, code and reference screenshots. The approved prototype baseline is 8575a2c. Inspect the current checkout and any later commits; do not reset it to that baseline. Work on the current branch in the normal checkout. Do not use planning skills or restart visual design.

First resolve only the open questions required for the next slice, record my answers in the handoff, and continue independent work while waiting. Recommendations are not approved answers. Do not ask again about settled interactions. Freeze shared contracts before dependent workers begin.

Promote the approved UI into src/ServiceBusEmulatorExplorer.App using focused workflows and reusable Core services. Start with one working read-only slice: settings/connections, protected preferences, entity discovery/counts, paging, selection and inspector. Then integrate global search, Watch/tray/notifications, replay and typed-confirmation deletion. Preserve the prototype's exact behavior and styling, including no top accent line and only a neutral bottom toolbar divider. Do not ship synthetic broker behavior or copy the prototype's code-behind wholesale.

Run up to six workers concurrently when dependencies allow, with exact non-overlapping file ownership. You own shared contracts, shell/DI integration and runtime leases. Serialize Docker, shared build outputs, UI tests and external services. Use isolated test profiles and messages; never run destructive proofs against my ordinary data. Have an independent worker review each integrated slice, resolve findings and rerun affected checks.

Keep the handoff current with decisions, worker ownership, progress, evidence and milestone commits. Run meaningful unit, emulator and rendered UI checks; use the UI verification skill. Test Azure only with an authorized environment and report missing evidence honestly. Preserve the known emulator count limitation without inventing totals or skipping sources reported as zero.

Continue through the approved production checklist, asking only when a consequential unresolved decision or external blocker requires me. Commit completed milestones on the current branch. Do not merge, tag or publish a release. At handoff report completed slices, checks, remaining gaps and the exact next step.
```
