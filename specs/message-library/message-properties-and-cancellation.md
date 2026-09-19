# Extended message properties and scheduled cancellation

Status: approved scope extension to the [Message Library plan](../message-library-implementation-plan.md). This document defines the detailed contracts for its six added capabilities. Read it when implementing Stages 1–4. It contains specifications for future work, not implemented behavior.

## Scope and ownership

Include TTL, optional custom MessageId, ReplyTo/ReplyToSessionId, PartitionKey, the scalar application-property types below, and explicit cancellation of acknowledged schedules. No new library is needed. Keep the existing SDK pins, sequential dispatch, no automatic retries and shared local-file format.

Core `MessageLibrary/MessagePropertyCompiler.cs` owns property expression validation/materialization; `ApplicationPropertyCodec.cs` owns declared scalar type parsing/formatting; `TemplateCapture` owns the observed-property allowlist. SDK mapping stays in `TemplateRunSender`. Add focused Core `ServiceBus/ScheduledMessageCanceller.cs` and App `MessageLibrary/ScheduledCancellationWorkflow.cs` rather than embedding a second mutation workflow into the window or the send loop. Coordinator retains DI/connection-lifecycle integration. DTO names may follow repository conventions, but these responsibilities must remain distinct and independently testable.

## Version and schema extension

Extend **unreleased schemaVersion 1**, not a fictitious migration from an implemented version. Existing example files with omitted optional members remain valid. After this schema ships, incompatible changes require a real version/migration decision.

Inside `properties`, add these optional members:

| Member | JSON representation | Default |
|---|---|---|
| `messageId` | `{ "mode": "generated" }` or `{ "mode": "custom", "source": "order-$(CustomerId)" }` | Generated when absent |
| `timeToLive` | null or `{ "source": "00:10:00" }` | Inherit broker entity TTL |
| `replyTo` | null or a string expression | Unset |
| `replyToSessionId` | null or a string expression | Unset |
| `partitionKey` | null or a string expression | Unset |

Unknown members/modes remain errors. Generated-ID mode must not carry source. Custom mode requires a source. Metadata expressions use the existing token/escape grammar; no property-name interpolation or recursive expansion. Reply/partition/ID string expressions use string variables only. TTL and non-string application-property sources allow either a literal or exactly one token; the resolved scalar text is passed to the explicit type parser. JSON object/array/null variable values cannot be coerced into metadata scalars. Defaults apply at variable resolution, not after a malformed property parse.

All these properties can be authored offline and stored in a repository. Reply paths are application metadata, not a connection profile or the message's actual destination. They do not create entities, send a reply automatically or select credentials. The user reviews them when choosing the actual queue/topic. Absolute schedule instants and cancellation receipts remain local run state, never template fields.

## MessageId and duplicate detection

Generated is the default and retains one frozen GUID per prepared message. Custom mode evaluates its expression once per row and preserves it exactly. Require a nonempty/non-whitespace value with at most 128 UTF-16 code units, and validate the pinned SDK setter; reject leading/trailing whitespace with an actionable error instead of trimming. IDs may contain ordinary business punctuation. Use ordinal comparisons.

Switching ID mode/source invalidates preparation; navigation, review, transport preflight and scheduling-time edits do not regenerate IDs. Capture always defaults to Generated and does not copy an observed ID. To reuse one deliberately, the user selects Custom and types/pastes it. Show the final ID in row preview and every outcome/export.

Do not forbid deliberate duplicate IDs: this tool must support duplicate-detection testing. On a prepared CSV run, group repeated custom IDs and show affected row numbers. Review requires an explicit unchecked acknowledgement when custom IDs repeat within the run. Regardless of whether local duplicates exist, custom mode always displays that a previously used ID can be accepted and discarded by a deduplicating entity. Detecting local duplicates uses MessageId alone, conservatively; show partition keys alongside them rather than claiming this is the broker's exact deduplication key.

Existing typed discovery exposes `RequiresDuplicateDetection`; present Yes/No/Unknown when available without adding a namespace-wide administration requirement. An SDK acknowledgement means the operation was accepted, not that a distinct retained copy or delivery was created. Keep the user-facing `Confirmed sent` / `Confirmed scheduled` outcome labels with that definition and a visible duplicate-detection note; never manufacture a `Deduplicated` result that the API did not report. No automatic retry, ID rewriting, broker deduplication configuration changes, or exactly-once guarantee.

The broker may use MessageId alone or MessageId plus PartitionKey depending on partitioning, and scheduled submissions participate too. These facts motivate the review warning; they do not authorize changing the broker configuration. [Duplicate detection](https://learn.microsoft.com/en-us/azure/service-bus-messaging/duplicate-detection).

## TTL

Use an explicit choice: Inherit entity default or Specify duration. `timeToLive.source` uses invariant TimeSpan `c` format (`[d.]hh:mm:ss[.fffffff]`), with a positive duration and whole-millisecond precision. Zero, negative, unparseable, overflow or sub-millisecond values fail. The UI may expose days/hours/minutes/seconds, but the persisted contract and CSV mapping use this duration format. This is a relative lifetime, never an absolute expiry timestamp.

Compile/materialize TTL once per row and include it in the frozen envelope and property preview. Map a specified value to `ServiceBusMessage.TimeToLive`; leave it unset for inherit mode. Report the selected entity's known default/ceiling alongside the requested duration using available metadata; if unknown, label it unknown. Do not silently clamp or rewrite the template. A requested value above a known ceiling is a visible warning at Review, not a fabricated claim that the broker will honor it. No need to add an extra acknowledgement checkbox just for this warning.

For scheduled messages, lifetime begins at broker enqueue/activation, not when the app schedules the message. Show requested duration and due time separately. Any calculated expiry preview is explicitly an estimate, never a broker-observed fact. Tests should verify eligibility by receiving, not disappearance from peek or a counter; expiration can be lazy. [TTL semantics](https://learn.microsoft.com/en-us/azure/service-bus-messaging/message-expiration).

Capture defaults to Inherit to avoid silently carrying an old environment's expiration policy. Offer `Copy observed TTL` with the actual observed duration, clearly distinguished from remaining lifetime. Only enable this option when the observed TimeSpan satisfies the supported format/precision/range. Preserve that duration if selected; never compute it from `ExpiresAt - now`. An unsupported or unavailable observed value is visible and can be replaced manually. Existing `SystemProperties["TimeToLive"]` is an observed typed value; any future move to a typed projection must preserve the existing inspector contract.

## Reply routing and PartitionKey

`replyTo` and `replyToSessionId` preserve exact nonblank strings; blank values map to unset. Maximum reply session ID is 128 UTF-16 code units, followed by SDK validation. If replyToSessionId is set without replyTo, report an authoring error rather than silently dropping the session. Do not enforce an arbitrary queue-name regex on ReplyTo: relative entity paths and absolute addresses are application-defined. No network lookup or implicit response consumer is created.

`partitionKey` lives in the Advanced properties section. Preserve exact nonblank text, with the pinned SDK's 128-character bound. Normalize only blank input to unset. If both SessionId and PartitionKey are set, require ordinal equality before SDK construction; never allow setter order to silently override the author's value. When only SessionId is set, retain that explicit input and explain that it supplies partition affinity where applicable. Set validated SessionId before a matching PartitionKey. The same materialization rules apply to manual input and CSV rows.

Capture supported ReplyTo/ReplyToSessionId/PartitionKey as literal metadata, escaping token-like content. Do not copy TransactionPartitionKey or To. Show source field values in the capture review so a user can clear environment-specific reply paths/keys. Field values alone do not select or create destinations.

The editor supports PartitionKey regardless of connection so shared templates can be edited offline. For a known emulator connection with a nonempty PartitionKey, block transport review with a specific unsupported-capability message and an Edit properties action. Do not silently remove it. Cloud targets retain SDK/service validation; do not infer partition configuration merely from the presence of a key. The emulator cannot prove partition routing. Require deterministic SDK-mapping tests and, where an authorized cloud fixture exists, separate partition/session behavioral proof. If cloud proof is unavailable, record the cloud-partition behavior gate as BLOCKED, not passed by emulator tests. [SDK property rules](https://raw.githubusercontent.com/Azure/azure-sdk-for-net/Azure.Messaging.ServiceBus_7.20.1/sdk/servicebus/Azure.Messaging.ServiceBus/src/Primitives/ServiceBusMessage.cs), [partitioning](https://learn.microsoft.com/en-us/azure/service-bus-messaging/service-bus-partitioning).

## Scalar application-property contract

Keep descriptors `{ "type": "...", "source": "..." }`. The complete v1 scalar allowlist is below. Preserve its declared CLR type when creating the SDK message; do not collapse all numbers to Int64/Double. `source` is always a JSON string, protecting UInt64/decimal precision in the template file. The variable type system remains string/number/boolean/json; use a string input for type-specific formats, or a compatible scalar token. Display the resulting CLR type and canonical value in Preview.

| Tags | CLR target and input/output rules |
|---|---|
| `string` | String; exact characters and interpolation; no automatic trimming. |
| `boolean` | Boolean; literal `true` or `false` only. |
| `byte`, `sbyte`, `int16`, `uint16`, `int32`, `uint32`, `int64`, `uint64` | Corresponding CLR integer; invariant base-10 digits with optional minus for signed types only. No exponent, grouping, plus or fractional part. Checked range. Canonical decimal output. |
| `single`, `double` | Single/Double parsed invariantly from JSON-number syntax; finite results only. Reject overflow/underflow to zero from a nonzero input. Preview round-trip `R` spelling so explicit floating-point rounding is visible. |
| `decimal` | Exact System.Decimal. Invariant plain decimal syntax, optional minus, no exponent/grouping/plus. Parse coefficient/scale explicitly with BigInteger before Decimal construction: scale 0–28 and coefficient at most 96 bits. Reject excess precision/overflow instead of letting Decimal.TryParse round. Preserve representable scale in persisted/captured source; preview invariant decimal spelling. BigInteger is in the framework, no new package. |
| `char` | Exactly one non-surrogate UTF-16 code unit; supplementary characters belong in string, not Char. |
| `guid` | Guid in D format; accept upper/lowercase hex, canonical lowercase D output. |
| `dateTimeUtc` | DateTime, UTC kind; ISO UTC input with 0–7 fractional digits. Normalize to whole milliseconds before freezing to match AMQP timestamp precision; Preview shows seven-digit UTC text and explicitly flags discarded sub-millisecond precision. No missing timezone/local inference. |
| `dateTimeOffset` | DateTimeOffset; ISO date/time with explicit Z or numeric offset and 0–7 fraction digits. Normalize to UTC before freezing and show the input-to-UTC conversion; canonical round-trip `O`. The pinned SDK encodes UTC ticks, not the original offset. |
| `timeSpan` | TimeSpan invariant `c`, including signed/zero values and ticks; unlike TTL, no positivity or millisecond constraint. |
| `uri` | Absolute Uri parsed without fetching it; use AbsoluteUri canonical text, show that normalization in Preview. Reject relative URI values. |

For non-string sources, preserve neither unintended surrounding whitespace nor automatic culture-dependent conversion: reject it with an error. Char is the exception where a single space is a real character. Existing generated GUID and UTC variables can feed guid/dateTimeUtc descriptors respectively. Parser diagnostics identify property name, requested type and logical CSV row without logging the entire record. DateTime's wire-precision rule follows the [AMQP timestamp definition](https://docs.oasis-open.org/amqp/core/v1.0/amqp-core-types-v1.0.html#type-timestamp); type=string remains available when the application requires exact textual timestamps instead.

No `byte[]`, ArraySegment, Stream, object graphs, arrays, null application-property values or arbitrary CLR object deserialization in v1. Those exclusions remain visible on capture. They are distinct from approving richer scalar types. Application-property binary values have a documented service issue; streams also violate the immutable bounded preview model. A user may deliberately author a base64 string but the app does not silently convert binary metadata. [SDK application-property types and limitation](https://learn.microsoft.com/en-us/dotnet/api/azure.messaging.servicebus.servicebusmessage.applicationproperties?view=azure-dotnet).

Capture selects the corresponding tag based on the **observed CLR type**, not a guess from its text. Preserve integer widths, UInt64 and Decimal if present, Single/Double, Guid, TimeSpan, Char and ordinary string/bool. Convert DateTime Kind=Local to UTC explicitly; Kind=Unspecified is excluded until the user supplies a timezone/value. Apply the date/time and Uri normalizations above and list them in capture review. Nonfinite floats and unsupported values require acknowledgement of exclusion. Wire protocols/SDK decoding may normalize types: preserve what the receiver exposes and do not claim recovery of the original producer's CLR type. SDK round-trip tests must record actual type/value mappings rather than change a received type by heuristic. Pinned [AMQP conversion source](https://raw.githubusercontent.com/Azure/azure-sdk-for-net/Azure.Messaging.ServiceBus_7.20.1/sdk/servicebus/Azure.Messaging.ServiceBus/src/Amqp/AmqpMessageConverter.cs) defines DateTimeOffset as UTC ticks, Uri as AbsoluteUri, and TimeSpan as ticks.

Use the SDK's documented mapper for final wire conversion. Allow at most 128 application properties per template. At transport preflight, make body-empty SDK-message clones of the frozen metadata and measure them using the public ServiceBusMessageBatch.TryAddMessage/SizeInBytes APIs: a clone with one application property and mandatory ID must fit within a conservative 32 KiB envelope ceiling; the clone with every authored system/application property must fit within 60 KiB. These measurements include AMQP/batch overhead and therefore intentionally underuse the documented 32 KiB property/64 KiB header limits. They are app policy bounds, not exact header sizes. Then perform the ordinary full-message destination-size preflight. Dispose probes; never mutate the frozen input. No internal AMQP reflection, custom wire serializer or new package is needed. Tests cover these app bounds, multibyte strings and whole-message size independently; service rejection can still occur and retains its normal outcome. [Service limits](https://learn.microsoft.com/en-us/azure/service-bus-messaging/service-bus-quotas).

Exact-preview guarantees cover body bytes and materialized user-authored fields. SDK-injected trace metadata and broker-assigned fields are not authored template values; show them only as observed metadata when available, and exclude prior trace IDs on capture. Do not claim that SDK/broker envelope fields are byte-identical to a user preview.

## Cancellation workflow

Cancellation acts on acknowledged schedule receipts retained in the current session's run report. It is a separate destructive action, **Cancel scheduled messages**, not the Stop button and not DLQ deletion. Select one or more eligible report rows; show namespace, exact queue/topic, due UTC/local time, receipt and count in confirmation. Canceling the confirmation performs no SDK calls. Do not cancel by MessageId, source-inspector sequence number, template ID or current tree selection.

Eligibility: original dispatch outcome Confirmed scheduled, a real returned Int64 receipt, due instant still in the future, and no prior acknowledged cancellation. Unknown scheduling without a receipt cannot be canceled by guessing. Bind the cancellation operation to the receipt's canonical namespace/endpoint (including emulator host/port identity) and EntityAddress. Require the selected connected profile to target that same identity; a similar display name is insufficient. A reconnect to the same endpoint may create a fresh reviewed cancellation binding/generation. Changing to a different namespace does not retarget the receipt. If profile switching/disconnect occurs while canceling, use the same Stop-and-account lifecycle gate as sending.

Use a dedicated SDK client with MaxRetries=0/TryTimeout=30s and one sender for the captured destination. Call `CancelScheduledMessageAsync(receipt, token)` sequentially per selected row. Recheck generation, eligibility and due time before each call. If the time has elapsed, mark that attempt `NoLongerEligible`, stop, and leave remaining rows `NotAttempted`. If a call was already in flight, record the actual response/uncertainty even if the clock passes due. Stop at the first rejection/unknown; preserve previous acknowledgements. Prevent double-click/concurrent send-and-cancel mutations. Dispose failures cannot reverse an acknowledged operation.

Keep dispatch history immutable and store cancellation attempts separately:

```csharp
// Example domain contract, not shipping code.
record ScheduledCancellationAttempt(
    Guid AttemptId, Guid RunId, int RowIndex, long ScheduledSequenceNumber,
    DateTimeOffset RequestedAtUtc, DateTimeOffset? CompletedAtUtc,
    CancellationOutcome Outcome, string? ErrorCode);
// NotAttempted, InProgress, Acknowledged, Failed, OutcomeUnknown, NoLongerEligible
```

SDK success maps to **Cancellation acknowledged**, never a claim that a racing activation was reversed. SDK-documented definite rejection maps to Failed with sanitized code; timeout/connection loss/cancellation after invocation maps to Outcome unknown. A pre-invocation stop is NotAttempted. There is no automatic retry. A user may explicitly Retry cancellation for Failed/Outcome unknown only while the receipt is still future/otherwise eligible, with fresh confirmation and a new attempt record. Acknowledged attempts disable further app cancellation for that receipt. A successful cancellation request can race broker activation; absence from a peek result is not sufficient proof of cancellation. [Cancellation API](https://learn.microsoft.com/en-us/dotnet/api/azure.messaging.servicebus.servicebussender.cancelscheduledmessageasync?view=azure-dotnet), [activation race](https://learn.microsoft.com/en-us/azure/service-bus-messaging/message-sequencing#scheduled-messages).

Export a `cancellationAttempts` array for each report row (empty when none), with camelCase fields matching the record. Encode scheduledSequenceNumber as invariant decimal string, all instants as UTC seven-digit ISO, terminal outcome as `notAttempted`, `acknowledged`, `failed`, `outcomeUnknown` or `noLongerEligible`. An interrupted exported in-progress attempt is `outcomeUnknown`, never acknowledged. Retain scheduling receipt and original outcome unchanged in the same report. Export failure is separate from broker outcome.

No receipt-import or general scheduled-message browser is added. Closing the process or explicitly discarding the report removes the app's cancellation access to those session receipts, not the broker's schedules. Explain this and offer the existing JSON export before discarding. Beginning a new preparation must not silently discard the previous report; replacing a retained report requires explicit discard/export choice. Users can keep the app open to manage that report. Durable history/import is a separate future scope, not an implementation shortcut hidden inside preferences.

## UI and stage proof

Properties tab: retain the existing common fields; add Message ID mode/expression, TTL mode/duration and ReplyTo/reply session. Put PartitionKey in a named Advanced section. Application properties use a type dropdown and source editor. Validation errors remain attached to the affected field and appear in CSV row diagnostics. Review shows resolved IDs, requested TTL/inheritance, reply metadata, partition/session relationship and duplicate warnings. No new window arrangement is proposed; the approved workspace layout stands.

Add stable automation IDs: `TemplateMessageIdMode`, `TemplateMessageIdSource`, `TemplateTtlMode`, `TemplateTtlSource`, `TemplateReplyTo`, `TemplateReplyToSessionId`, `TemplatePartitionKey`, `TemplateDuplicateAcknowledgement`, `ScheduledCancelSelection`, `ScheduledCancelConfirm`, `ScheduledCancelStop`, `ScheduledCancellationResults`. Exercise enabled/disabled, validation, dirty, pending, failed and completed states at all three supported sizes. Cancellation confirmation uses the existing destructive-dialog visual conventions; SDK timeouts start after the modal closes.

| Risk | Canonical owner | Required red tests / acceptance | Stage / final evidence |
|---|---|---|---|
| R15 — property loss, precision or misleading expiry | ApplicationPropertyCodec + MessagePropertyCompiler + TemplateCapture | Every scalar tag/CLR mapping and boundary; UInt64 max, decimal max/scale28/excess precision, float overflow/underflow, UTC/offset/unspecified times, literal escapes; TTL inherit/positive/ceiling/activation; reply metadata; capture exclusions and normalizations visible | S1 schema/editor; S2 materialization/capture/send; S3 CSV parity; Pending |
| R16 — ID/partition changes or misleading duplicate success | MessagePropertyCompiler + TemplateSendWorkflow | Generated/custom freeze; ID length/whitespace; duplicate acknowledgement blocks send until checked; same/different session/partition; emulator key blocked; SDK mapping exact; acknowledged duplicate never claimed as a retained copy; cloud proof separately scoped | S2 single + S3 CSV; Pending |
| R17 — wrong-target or falsely confirmed cancellation | ScheduledCancellationWorkflow + ScheduledMessageCanceller | Receipt/endpoint binding; missing/elapsed receipt; denied dialog means zero calls; fresh generation after reconnect; Stop before/during; ack then disposal failure; reject/timeout; explicit retry history; activation race wording; original dispatch/export retained including >2^53 receipt | S2 single + S3 selection; Pending |

Environment/bounds: existing run/file/CSV limits apply. Cap stored cancellation attempts at ten per row, after which further attempts are disabled with an explanation; this bounds session memory without deleting evidence. Source tests use injected TimeProvider and fake sender; authorized integration uses isolated queue/topic fixtures. Success proof: cancel a future schedule well before due, then use bounded receive after due and a retained control message to demonstrate the fixture operated; do not rely only on counters/peek. Timing-sensitive races are deterministic adapter tests, not flaky deadlines. No tests run against ordinary schedules.

Each new risk must be reconciled in Stage 4. Complete schema/editor support in Stage 1, single-message property/send/cancel flow in Stage 2, CSV materialization and selected cancellation in Stage 3; do not append a disconnected cleanup stage that leaves the main path incomplete. The [advanced example](examples/order-created-advanced.sbetemplate.json) covers every added authoring field and representative rich scalars; its explicit PartitionKey makes it a cloud-oriented fixture, intentionally blocked against the emulator until the user clears that field.

Pinned API evidence: `Azure.Messaging.ServiceBus` 7.20.1 exposes both singular `CancelScheduledMessageAsync(long, CancellationToken)` and plural `CancelScheduledMessagesAsync(IEnumerable<long>, CancellationToken)`. The singular wrapper delegates to the plural API with one sequence number; this plan deliberately uses the singular call for per-row accounting. See [pinned sender source](https://raw.githubusercontent.com/Azure/azure-sdk-for-net/Azure.Messaging.ServiceBus_7.20.1/sdk/servicebus/Azure.Messaging.ServiceBus/src/Sender/ServiceBusSender.cs), lines 496–502.
