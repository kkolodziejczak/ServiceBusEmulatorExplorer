# Daily audit remediation — 2026-09-19

The [original audit](daily-workflow-audit-2026-09-19.md) and its [66-case results](daily-workflow-audit-2026-09-19-results.md) remain historical evidence for production revision `7f0de3b` and audit commit `3de653c`. This follow-up is authorized to fix the failures while respecting emulator limitations.

## Diagnosis and bounded change

| Hypothesis | Diagnostic and result |
| --- | --- |
| Refresh does not update discovery | Rejected: Refresh calls discovery and applies the snapshot before peeking the visible page. Direct administration calls independently return the same zero values. |
| Emulator count observations are unreliable | Confirmed: independent peeks return seeded deliveries while raw administration counters return zero. The committed audit has seven failing assertions across provider, workspace and actual WPF surfaces. |
| Scheduled time is missing from the broker | Rejected: the raw received message exposes its future ScheduledEnqueueTime. |
| Projection drops scheduled inspection metadata | Confirmed: the production system-property projection omits the timestamp; the live audit fails after independently reading that timestamp from the broker. |

The implementation keeps real-broker count behavior and represents emulator counters as unavailable through the existing count-availability model. Detection reads the exact development-emulator option in the administration connection string with `DbConnectionStringBuilder`, not hostname matching or a guess based on a zero value. Queue and topic scheduled counters receive the same treatment; subscription scheduled counts remain unsupported as before. This capability limitation does not imply incomplete entity discovery.

Scheduled inspection gains the broker's UTC scheduled enqueue timestamp in system properties, which the existing Properties tab renders. No layout redesign, message mutation or alternative count scan is introduced.

Independent review caught an intermediate regression: skipping runtime-property requests also removed creation/update dates. A new regression failed for queues, topics and subscriptions before the repair. The final implementation still requests those properties and retains their metadata, masking only the counters. Actual request failures continue to produce safe discovery issues; the emulator count limitation alone does not.

## Test contract correction

The original `Runtime_counts_match_the_messages_visible_to_the_user` case demanded behavior from Microsoft's emulator that this application cannot repair. Its replacement, `Emulator_counts_are_unavailable_while_messages_remain_readable`, still sends and independently reads two real messages, logs the raw count observation, and requires production discovery to expose unavailable/null counts with an emulator explanation. It does not skip the provider branch, accept fabricated totals, or require the emulator to keep returning zero forever.

The other existing daily cases remain in the suite. The scheduled-time assertion is strengthened to verify the exact named field and UTC timestamp. Real-window count checks now require the unavailable marker in both the message summary and navigation tree at 1500×1000, 1100×800 and 980×640. An additional rendered-WPF integration case verifies the Properties tab displays the scheduled due timestamp.

The added Properties case initially used external UIA `TextPattern`, which returned empty text even after a bounded wait. A screenshot and UIA-tree diagnostic established that the selected message's Properties JSON was visibly rendered, while the external Document provider still returned empty text. The final case therefore opens the production window on a WPF dispatcher against the real broker, toggles the real Properties control through its automation peer, and inspects the rendered `FlowDocument` with `TextRange`. Exact field/date/time assertions remain. The six original desktop audit cases still launch the separate executable through FlaUI. No test-only production seam or clipboard mutation was added.

## Verification

Final result: **67 daily cases passed, 0 failed, 0 skipped**. This comprises the original 66 cases (with the provider contract correction explained above) plus one rendered Properties case. Including the other regression suites, **567 test cases passed**. Parameterized unit-test rows are counted separately even when their display names are truncated identically.

| Final evidence | Passing cases counted | Scope |
| --- | ---: | --- |
| [workflows-final.trx](../artifacts/daily-fix/results/workflows-final.trx) | 45 | Browse 15, search/lifecycle 17, safety 12, rendered Properties 1 |
| [windows-final.trx](../artifacts/daily-fix/results/windows-final.trx) | 10 | Original desktop audit 6 and existing desktop journeys 4 |
| [broker-final.trx](../artifacts/daily-fix/results/broker-final.trx) | 31 | Daily operations 16 and existing broker cases 15 |
| [core-final.trx](../artifacts/daily-fix/results/core-final.trx) | 188 | Core unit/regression cases |
| [app-final.trx](../artifacts/daily-fix/results/app-final.trx) | 293 | App unit/render cases |

`windows-final.trx` also contains the failed external-UIA attempt for the added Properties case. That one result is superseded by the same case's passing rendered-document proof in `workflows-final.trx`; it is not counted twice or silently omitted. Earlier `daily-ui-workflows.trx` and `properties-diagnostic.trx` are diagnostic/superseded runs. Original red evidence remains in `artifacts/daily-audit/results`. The metadata regression's explicit red evidence is [metadata-red.trx](../artifacts/daily-fix/results/metadata-red.trx), followed by its passing case in `core-final.trx`.

The final solution build completed with **0 warnings and 0 errors**. Production and test changes were reviewed independently after the metadata repair; the review outcome is recorded below.

Independent read-only review: **PASS on Spec and Standards/Quality**, no actionable findings. The metadata regression is closed by explicit red/green proof. The review checked the final diff, executed-test artifacts, emulator/cloud branches, empty topics, timestamp projection, test-contract correction and diagnostic cleanup. `git diff --check` passed and all 12 local Markdown links in the affected documents resolved.

Runtime: isolated Compose project `sbe-daily-fix`, runtime port 5674 and administration port 5302, using the same pinned emulator/SQL images as the original audit. Broker suites ran serially. The user's demo runtime and already-open app were left untouched. Builds used `--artifacts-path artifacts/daily-fix/final` to avoid the running app's binaries.

After verification, only the `sbe-daily-fix` containers and network were removed. Generated local evidence remains under ignored `artifacts/` paths.

### Rendered UI verification

| Gate | Result | Evidence |
| --- | --- | --- |
| Authority/readiness | PASS | Latest user request, existing unavailable-count presentation and Properties tab; synthetic entities in an isolated emulator. |
| Count state and dependent updates | PASS | External send → Refresh → loaded row with unavailable tree/summary counts; DLQ navigation, automatic refresh, selection preservation and reopen. |
| Geometry/responsiveness | PASS | Active and DLQ captures at 1500×1000, 1100×800 and 980×640; tree counts, summary and Refresh remain visible. |
| Scheduled properties | PASS | Production window and real scheduled broker message; rendered FlowDocument contains the scheduled UTC field and due date/time. |
| Accessible controls/focus | PASS (scoped) | UIA names/patterns operate Refresh, bucket tabs and row selection; rendered focus visible. |
| External rich-text accessibility | BLOCKED | UIA text extraction is empty despite visible JSON; screen-reader impact remains unverified. |
| Regression/clean close | PASS | Full affected daily journeys rerun; temporary diagnostic code removed; fixtures use isolated profiles and unique broker entities. |

Six count screenshots are under `artifacts/daily-fix/final/bin/ServiceBusEmulatorExplorer.UiSmoke.Tests/debug/daily-fix-captures`. The external UIA diagnostic screenshot remains a local artifact under `artifacts/daily-fix/properties-probe`, not a production asset. This is a scoped functional/render verification, not a complete accessibility certification.

## Residual scope

The original audit's Azure authorization, network fault, scale, long-running operation, physical accessibility and mixed-DPI limits remain. The emulator adaptation is not a claim that the cloud's eventually consistent counters are an atomic snapshot of a concurrently changing queue.

An older emulator that fails the runtime-properties request entirely can still produce incomplete metadata discovery. This is deliberately reported instead of suppressing a real request failure. Live Azure was not exercised; its numeric-count behavior is covered with SDK model fixtures and the non-emulator capability branch.

External UIA text extraction from the Properties rich-text control returned empty in this desktop environment despite visible text. Its root cause and screen-reader impact were not established. Rendered document verification passes through the in-process WPF path; that does not prove external assistive-technology text access.

## Final daily cases

| Case | Result | Evidence |
| --- | --- | --- |
| DailyOperationsAuditTests.Active_message_replay_is_rejected_before_broker_mutation | Passed | broker-final.trx |
| DailyOperationsAuditTests.Canceled_replay_scan_leaves_the_original_dlq_delivery_available | Passed | broker-final.trx |
| DailyOperationsAuditTests.Edited_replay_preserves_explicit_valid_json_and_changes_only_requested_body | Passed | broker-final.trx |
| DailyOperationsAuditTests.Empty_dlq_delete_request_is_a_safe_no_op_without_mutation | Passed | broker-final.trx |
| DailyOperationsAuditTests.Emulator_counts_are_unavailable_while_messages_remain_readable | Passed | broker-final.trx |
| DailyOperationsAuditTests.Expired_message_is_not_receivable_even_when_diagnostic_peek_retains_it | Passed | broker-final.trx |
| DailyOperationsAuditTests.Partial_dlq_delete_reports_stale_target_and_deletes_only_confirmed_target | Passed | broker-final.trx |
| DailyOperationsAuditTests.Queue_replay_copy_preserves_body_properties_and_original_until_delete | Passed | broker-final.trx |
| DailyOperationsAuditTests.Repeated_replay_attempts_use_distinct_message_ids_and_keep_source | Passed | broker-final.trx |
| DailyOperationsAuditTests.Scheduled_delivery_inspection_preserves_the_broker_scheduled_enqueue_time | Passed | broker-final.trx |
| DailyOperationsAuditTests.Scheduled_message_can_be_peeked_before_due_and_received_after_due | Passed | broker-final.trx |
| DailyOperationsAuditTests.Selective_dlq_delete_removes_only_selected_delivery_and_keeps_active_messages | Passed | broker-final.trx |
| DailyOperationsAuditTests.Stale_dlq_target_reports_failure_without_deleting_a_new_neighbor | Passed | broker-final.trx |
| DailyOperationsAuditTests.Topic_replay_fans_out_to_every_subscription_and_keeps_source_dlq | Passed | broker-final.trx |
| DailyOperationsAuditTests.Watch_baselines_existing_active_messages_and_reports_one_new_arrival | Passed | broker-final.trx |
| DailyOperationsAuditTests.Watch_reports_a_new_dead_letter_arrival_separately_from_active_baseline | Passed | broker-final.trx |
| DailyBrowseAuditTests.Active_and_dead_letter_rows_with_the_same_message_id_have_distinct_buckets | Passed | workflows-final.trx |
| DailyBrowseAuditTests.Active_discovery_count_never_claims_zero_when_peek_observes_messages | Passed | workflows-final.trx |
| DailyBrowseAuditTests.Connect_and_open_empty_queue_shows_empty_page_without_failure | Passed | workflows-final.trx |
| DailyBrowseAuditTests.Dead_letter_page_and_discovery_count_agree_with_independent_peek | Passed | workflows-final.trx |
| DailyBrowseAuditTests.Expired_messages_remain_inspectable_with_expiry_metadata_or_stale_marker_after_refresh | Passed | workflows-final.trx |
| DailyBrowseAuditTests.External_send_then_refresh_adds_the_new_message | Passed | workflows-final.trx |
| DailyBrowseAuditTests.Message_body_and_properties_survive_browse_projection | Passed | workflows-final.trx |
| DailyBrowseAuditTests.Queue_pagination_reaches_the_end_without_duplicates_or_gaps | Passed | workflows-final.trx |
| DailyBrowseAuditTests.Refresh_retains_a_focused_row_when_the_broker_no_longer_returns_it | Passed | workflows-final.trx |
| DailyBrowseAuditTests.Repeated_refresh_is_non_consuming_and_preserves_the_visible_set | Passed | workflows-final.trx |
| DailyBrowseAuditTests.Same_message_id_in_two_subscriptions_remains_two_distinct_deliveries | Passed | workflows-final.trx |
| DailyBrowseAuditTests.Subscription_discovery_count_never_claims_zero_when_peek_observes_messages | Passed | workflows-final.trx |
| DailyBrowseAuditTests.Switching_entities_clears_rows_from_the_previous_source | Passed | workflows-final.trx |
| DailyBrowseAuditTests.Topic_browse_paginates_across_subscriptions_without_losing_source_identity | Passed | workflows-final.trx |
| DailyBrowseAuditTests.Topic_discovery_count_never_claims_zero_when_subscription_peek_observes_messages | Passed | workflows-final.trx |
| DailySafetyAuditTests.Active_delivery_cannot_be_deleted_through_dlq_command | Passed | workflows-final.trx |
| DailySafetyAuditTests.Active_delivery_cannot_be_replayed_and_broker_is_unchanged | Passed | workflows-final.trx |
| DailySafetyAuditTests.Concurrent_replay_requests_reserve_distinct_attempts_and_keep_original | Passed | workflows-final.trx |
| DailySafetyAuditTests.Delete_canceled_before_start_settles_nothing | Passed | workflows-final.trx |
| DailySafetyAuditTests.Delete_of_previous_connection_delivery_is_rejected_after_reconnect | Passed | workflows-final.trx |
| DailySafetyAuditTests.Disconnected_replay_cannot_send_using_old_credentials | Passed | workflows-final.trx |
| DailySafetyAuditTests.Empty_delete_is_a_noop_without_clearing_loaded_delivery | Passed | workflows-final.trx |
| DailySafetyAuditTests.Failed_replay_reservation_persistence_prevents_broker_send | Passed | workflows-final.trx |
| DailySafetyAuditTests.Invalid_edited_json_blocks_replay_selection_and_discard_restores_eligibility | Passed | workflows-final.trx |
| DailySafetyAuditTests.Replay_canceled_before_start_sends_nothing_and_keeps_original | Passed | workflows-final.trx |
| DailySafetyAuditTests.Replay_of_previous_connection_delivery_is_rejected_after_reconnect | Passed | workflows-final.trx |
| DailySafetyAuditTests.Valid_edited_json_replays_changed_copy_and_preserves_original_bytes | Passed | workflows-final.trx |
| DailySearchLifecycleAuditTests.Browse_active_page_and_inspect_json_body | Passed | workflows-final.trx |
| DailySearchLifecycleAuditTests.Clear_search_restores_browse_scope_and_selection | Passed | workflows-final.trx |
| DailySearchLifecycleAuditTests.Dirty_inspector_draft_blocks_profile_switch_until_discard_is_confirmed | Passed | workflows-final.trx |
| DailySearchLifecycleAuditTests.Disconnect_clears_search_and_reconnect_starts_a_clean_browse_session | Passed | workflows-final.trx |
| DailySearchLifecycleAuditTests.Duplicate_message_ids_remain_distinct_deliveries | Passed | workflows-final.trx |
| DailySearchLifecycleAuditTests.Failed_connection_can_recover_by_switching_to_a_valid_profile | Passed | workflows-final.trx |
| DailySearchLifecycleAuditTests.Inspector_preserves_invalid_json_and_unicode_text | Passed | workflows-final.trx |
| DailySearchLifecycleAuditTests.Inspector_shows_non_utf8_body_as_base64_without_data_loss | Passed | workflows-final.trx |
| DailySearchLifecycleAuditTests.Invalid_query_is_reported_without_returning_rows | Passed | workflows-final.trx |
| DailySearchLifecycleAuditTests.Preferences_round_trip_updates_search_budget_and_timestamp_display | Passed | workflows-final.trx |
| DailySearchLifecycleAuditTests.Profile_warning_denial_preserves_session_and_approved_auto_connect_switches_profile | Passed | workflows-final.trx |
| DailySearchLifecycleAuditTests.Search_correlation_id_unions_queue_and_topic_subscription_deliveries | Passed | workflows-final.trx |
| DailySearchLifecycleAuditTests.Search_exact_message_id_returns_the_expected_delivery | Passed | workflows-final.trx |
| DailySearchLifecycleAuditTests.Search_no_match_completes_without_stale_rows | Passed | workflows-final.trx |
| DailySearchLifecycleAuditTests.Search_scope_filters_queue_topic_subscription_and_clear_restores_all_matches | Passed | workflows-final.trx |
| DailySearchLifecycleAuditTests.Search_stop_during_discovery_can_continue_into_message_scanning | Passed | workflows-final.trx |
| DailySearchLifecycleAuditTests.Unqualified_message_id_OR_search_ignores_correlation_only_matches | Passed | workflows-final.trx |
| DailyWindowAuditTests.Automatic_refresh_observes_external_arrival_without_clicking_refresh | Passed | windows-final.trx |
| DailyWindowAuditTests.Closing_and_reopening_restores_connection_selected_queue_and_fresh_messages | Passed | windows-final.trx |
| DailyWindowAuditTests.Dead_letter_navigation_does_not_present_zero_total_beside_observed_dead_letters | Passed | windows-final.trx |
| DailyWindowAuditTests.Empty_queue_then_external_send_and_manual_refresh_loads_without_consuming | Passed | windows-final.trx |
| DailyWindowAuditTests.Manual_refresh_does_not_present_zero_active_total_beside_observed_active_messages | Passed | windows-final.trx |
| DailyWindowAuditTests.Refresh_preserves_user_focused_message_after_external_arrival | Passed | windows-final.trx |
| DailyWindowAuditTests.Scheduled_due_time_is_visible_in_the_real_message_properties_inspector | Passed | workflows-final.trx |
