# Topic context amendment

Approved with the [five-board visual contract](design/README.md). Supersedes the original hidden-namespace-tree design. Associations are discovery hints, not credentials or permission to send.

## Schema and capture

Extend unreleased schemaVersion 1 with optional `associations`, default empty. Entry: `{ "entityKind": "topic", "entityPath": "order-events" }`. Kind accepts queue/topic, never subscription. Maximum 32 entries; reject duplicate kind/path keys using existing discovery identity comparison. Validate literal nonblank entity paths using EntityAddress conventions; do not silently trim. No URLs, credentials, profile IDs, namespace hostname, variable expressions or filesystem paths. Stable kind/path serialization; old examples without this field remain valid. Unknown fields still fail.

Resolve paths only within the currently active connection. A topic association is not a fully qualified send destination. Capture from a subscription explicitly proposes its parent topic; from a queue proposes that queue. Show the association in capture and allow editing/removal/addition in Properties before Save. Source messages are untouched.

## Shared context and filtered projections

App `MessageLibraryContext` owns filter origin, entity-key set, template identity and target resolution, composing existing discovery/selection and library index. Use one explicit user-action transition and update both projections; do not recursively drive tree-selection callbacks. Refresh/reconciliation is not a deliberate selection.

1. Enter Library from Investigation with All saved/no filter. A restored namespace highlight is not a fresh click. Preserve drafts and prior broker inspection context.
2. Deliberate queue/topic selection filters both trees. Subscription selection explicitly resolves to its parent topic and labels that topic. Keep only the matching entity and topic subscriptions in namespaces; only associated templates and their folder/root ancestors in the library. Hide unrelated folders. Search intersects the filter; unassociated templates appear only in All saved.
3. Deliberate template selection filters namespaces to its associations and library templates to intersecting associations. Multiple associations show all matching topics and do not arbitrarily select a send target. An unassociated template clears association filtering; target is unresolved unless explicitly selected for that draft.
4. Clearing the Namespaces search query restores both complete trees, preserving draft/template identity. Do not add a separate Clear filter/Show all row or immediately reapply the retained selection. A later explicit selection can filter again. Clearing browsing scope does not erase a still-valid visible target or grant approval.
5. If context hides the open template, retain the draft with “Outside current filter”; never retarget it silently to the clicked topic. Target remains unresolved after mismatch until explicit choice. Selecting another template uses Save/Discard/Cancel.
6. Missing entities show unavailable status, not fabricated counts or fuzzy name matches. Offline library filtering still works from association keys. Profile change clears target/review, re-resolves keys, preserves draft. Reconnect uses connection-generation guards.

## Target resolution and live refresh

One connected/discovered candidate consistent with the draft/context becomes read-only Send to. A subscription is never the SDK send address: show its parent topic explicitly. Multiple/absent/stale/incompatible candidates show the queue/topic picker and block Review. Explicit target choice does not rewrite associations. Final review always shows exact profile/endpoint/entity and retains session/partition/size checks. No context change may retarget an in-flight run or retained receipts, or regenerate frozen values.

Reuse existing namespace refresh/Watch services and intervals; no second polling implementation. Keep enabled refresh running during authoring and after send. Distinguish library-file Refresh from broker Refresh. Never optimistically increment counts from acknowledgements; counts alone do not prove consumption. Refresh failures are separate from send success and retain stale/unavailable indicators. Investigation Active/DLQ and preview remain accessible through its workspace with context preserved; no permanent live-inspection pane is added to Library.

## R18 — context drift and wrong destination

Canonical owners: MessageLibraryContext for projections/resolution; existing refresh service for observations; TemplateSendWorkflow for immutable reviewed target. No new framework/package.

Stage 1 proof: schema round-trip/old-empty, both filter directions, ancestor pruning, multiple topics, clear-without-reapply, missing/offline, refresh without selection loops, draft preservation. Stage 2: parent-topic capture; inferred target versus picker; mismatched draft; same path on another profile; stale callback/generation; refresh failure after send; zero send calls on invalid context. Stage 3: identical CSV behavior, stable IDs, retained schedule receipts unaffected by later filtering. Stage 4: production-control flow plus pixel-perfect A and review/error boards.

Acceptance: relevant branches only; All saved restores both; no loop/draft loss/implicit wrong-target send; explicit immutable review/receipt binding; truthful counts. R18 remains Pending until executed evidence exists.
