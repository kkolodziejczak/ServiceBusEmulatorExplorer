# Replay and inspector contract

Implemented contract: DEC-031 through the applicable later replay decisions in
[DECISIONS.md](../DECISIONS.md), with presentation defined by
[DESIGN.md](../DESIGN.md) and the approved
[Replay 47 board](message-library/design/proposed/replay-47-v1-opus-inline-details.png).

## Review, identity and persistence

- The DLQ Body is directly editable; Properties and non-DLQ bodies are read-only. Find,
  folding, Modified, Discard and viewport-fixed Copy preserve drafts and document content.
- Review shows the exact editable generated outgoing ID and selected body, without property
  editors. Preparation sends nothing. Confirmation revalidates connection generation,
  namespace, original fingerprint and attempt reservation under the existing mutation/save
  gates. Send the reviewed reservation once; cancel consumes nothing and preserves the draft.
- Persist the counter and conservative attempt metadata atomically before send, then record
  confirmed/not-sent outcomes. Failed outcome persistence retains uncertainty. History stores
  identities and allowlisted diagnostics, not message bodies, secrets or raw exception text.
  Settings and history operations share the protected store's atomic writer.

## History presentation and observations

- Modeless history is connection/namespace scoped. Original and replay magnifiers search exact
  IDs in Investigation; Find all replays uses visible replay IDs. History remains open. Topic
  fanout may yield several deliveries for one replay ID; counts must distinguish those concepts.
- Use Original message, Replay and Observed copies columns plus compact row-end cleanup.
  Original rows show source/DLQ and retained state. Replay rows show sent/requested time and
  outcome. State counts show DLQ first and preserve mixed, unchecked, incomplete, uncertain,
  not-sent and complete-absence distinctions. Use full dates and the user's zone/format.
- Only the selected row expands: full Original ID with adjacent Copy; horizontal wrapping
  source hierarchy and original state with Delete original; full Replay ID with adjacent Copy.
  Destination is a source tooltip, not another row. Applicable failure/coverage reasons remain
  accessible. The table is the only scrolling region; long IDs wrap at compact sizes.
- Status checks use fresh discovery and complete non-consuming Active/DLQ pagination, with
  bounded scans, time and coverage. Failure, cancellation, missing discovery or stale generation
  is incomplete, never Not found. Observation is not proof of API processing. Pending topic
  schedules and forwarding outside the namespace are outside covered absence checks.

## Cleanup safeguards

- Row-local Remove from history is eligible only after the latest complete covered scan reports
  Not found; revalidate attempt/profile/namespace and eligibility at action time. Confirmation
  says local history only. Failed persistence leaves the row visible. No bulk Clear history.
- Removal hides the entry durably, retaining internal identities, numbering, uncertain-send
  evidence and hidden siblings needed to block unsafe original cleanup. It never touches broker
  deliveries and is not secure erasure of stored replay lineage.
- Delete original is separate and never automatic. Require confirmed sends and fresh checks of
  every relevant attempt, current generation, exact source/sequence/fingerprint reacquisition
  and typed DELETE confirmation. Preserve uncertainty if settlement cannot be established.
- Ordinary message-list Delete acts only on checked deliveries, independent of inspector focus.

## Verification seams

ReplayWindowsTests and ReplayHistoryVisualContractTests exercise real routed WPF review,
modeless search/reopen, selected detail, full-ID copy, cleanup/cancel and compact layout.
ReplayHistoryCleanupTests and InvestigationReplayPersistenceTests cover hidden lineage,
store failure, profile isolation and restart. Core replay/observer tests cover exact IDs,
fanout, incomplete observations and targeting. Integration fixtures establish broker effects;
rendered proof alone does not establish physical input, screen-reader or multi-monitor behavior.
