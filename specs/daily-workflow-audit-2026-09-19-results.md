# Daily workflow audit: per-case results

66 new cases: **58 passed, 8 failed, 0 skipped/other**. Superseded harness runs are excluded. Production was not repaired.

See the [audit scope and decision tree](daily-workflow-audit-2026-09-19.md). Results below come from actual VSTest TRX records, not source inspection.

| Suite | Total | Passed | Failed |
| --- | ---: | ---: | ---: |
| DailyBrowseAuditTests | 15 | 11 | 4 |
| DailyOperationsAuditTests | 16 | 14 | 2 |
| DailySafetyAuditTests | 12 | 12 | 0 |
| DailySearchLifecycleAuditTests | 17 | 17 | 0 |
| DailyWindowAuditTests | 6 | 4 | 2 |

## Failed assertions

- **DailyOperationsAuditTests.Runtime_counts_match_the_messages_visible_to_the_user**: Assert.Equal() Failure: Values differ Expected: 2 Actual: 0
- **DailyOperationsAuditTests.Scheduled_delivery_inspection_preserves_the_broker_scheduled_enqueue_time**: The broker exposes ScheduledEnqueueTime but inspection projection loses it.
- **DailyBrowseAuditTests.Active_discovery_count_never_claims_zero_when_peek_observes_messages**: Discovery reported 0 while an independent peek observed 1. A broker limitation must be represented as unavailable.
- **DailyBrowseAuditTests.Dead_letter_page_and_discovery_count_agree_with_independent_peek**: Discovery reported DLQ 0 while an independent peek observed 1.
- **DailyBrowseAuditTests.Subscription_discovery_count_never_claims_zero_when_peek_observes_messages**: Subscription discovery reported 0 while subscription browse observed 1 deliveries.
- **DailyBrowseAuditTests.Topic_discovery_count_never_claims_zero_when_subscription_peek_observes_messages**: Topic discovery reported 0 while topic browse observed 2 deliveries.
- **DailyWindowAuditTests.Dead_letter_navigation_does_not_present_zero_total_beside_observed_dead_letters**: Assert.DoesNotContain() Failure: Sub-string found ↓ (pos 34) String: "1 loaded · 0 Active · 0 Scheduled · 0 DLQ" Found: "· 0 DLQ"
- **DailyWindowAuditTests.Manual_refresh_does_not_present_zero_active_total_beside_observed_active_messages**: Assert.DoesNotContain() Failure: Sub-string found ↓ (pos 9) String: "1 loaded · 0 Active · 0 Scheduled · 0 DLQ" Found: "· 0 Active"

## Executed new cases

### [DailyBrowseAuditTests](../tests/ServiceBusEmulatorExplorer.UiSmoke.Tests/DailyBrowseAuditTests.cs)

| Case | Result | Duration |
| --- | --- | --- |
| Active_and_dead_letter_rows_with_the_same_message_id_have_distinct_buckets | Passed | 00:00:08.3281995 |
| Active_discovery_count_never_claims_zero_when_peek_observes_messages | Failed | 00:00:05.5916487 |
| Connect_and_open_empty_queue_shows_empty_page_without_failure | Passed | 00:00:03.3464883 |
| Dead_letter_page_and_discovery_count_agree_with_independent_peek | Failed | 00:00:05.7394907 |
| Expired_messages_remain_inspectable_with_expiry_metadata_or_stale_marker_after_refresh | Passed | 00:00:14.2207777 |
| External_send_then_refresh_adds_the_new_message | Passed | 00:00:06.0424875 |
| Message_body_and_properties_survive_browse_projection | Passed | 00:00:05.6521138 |
| Queue_pagination_reaches_the_end_without_duplicates_or_gaps | Passed | 00:00:05.7103528 |
| Refresh_retains_a_focused_row_when_the_broker_no_longer_returns_it | Passed | 00:00:06.1935219 |
| Repeated_refresh_is_non_consuming_and_preserves_the_visible_set | Passed | 00:00:06.5758732 |
| Same_message_id_in_two_subscriptions_remains_two_distinct_deliveries | Passed | 00:00:06.1729762 |
| Subscription_discovery_count_never_claims_zero_when_peek_observes_messages | Failed | 00:00:06.0558477 |
| Switching_entities_clears_rows_from_the_previous_source | Passed | 00:00:06.0105859 |
| Topic_browse_paginates_across_subscriptions_without_losing_source_identity | Passed | 00:00:06.5088929 |
| Topic_discovery_count_never_claims_zero_when_subscription_peek_observes_messages | Failed | 00:00:06.3015636 |

### [DailyOperationsAuditTests](../tests/ServiceBusEmulatorExplorer.Integration.Tests/DailyOperationsAuditTests.cs)

| Case | Result | Duration |
| --- | --- | --- |
| Active_message_replay_is_rejected_before_broker_mutation | Passed | 00:00:02.9566078 |
| Canceled_replay_scan_leaves_the_original_dlq_delivery_available | Passed | 00:00:02.8367358 |
| Edited_replay_preserves_explicit_valid_json_and_changes_only_requested_body | Passed | 00:00:05.1353190 |
| Empty_dlq_delete_request_is_a_safe_no_op_without_mutation | Passed | 00:00:02.8720618 |
| Expired_message_is_not_receivable_even_when_diagnostic_peek_retains_it | Passed | 00:00:07.7974159 |
| Partial_dlq_delete_reports_stale_target_and_deletes_only_confirmed_target | Passed | 00:00:10.0220009 |
| Queue_replay_copy_preserves_body_properties_and_original_until_delete | Passed | 00:00:07.1865957 |
| Repeated_replay_attempts_use_distinct_message_ids_and_keep_source | Passed | 00:00:07.1890616 |
| Runtime_counts_match_the_messages_visible_to_the_user | Failed | 00:00:02.8273996 |
| Scheduled_delivery_inspection_preserves_the_broker_scheduled_enqueue_time | Failed | 00:00:02.9135630 |
| Scheduled_message_can_be_peeked_before_due_and_received_after_due | Passed | 00:00:08.8111548 |
| Selective_dlq_delete_removes_only_selected_delivery_and_keeps_active_messages | Passed | 00:00:05.0672629 |
| Stale_dlq_target_reports_failure_without_deleting_a_new_neighbor | Passed | 00:00:10.1717987 |
| Topic_replay_fans_out_to_every_subscription_and_keeps_source_dlq | Passed | 00:00:05.4032713 |
| Watch_baselines_existing_active_messages_and_reports_one_new_arrival | Passed | 00:00:02.9161984 |
| Watch_reports_a_new_dead_letter_arrival_separately_from_active_baseline | Passed | 00:00:03.0140289 |

### [DailySafetyAuditTests](../tests/ServiceBusEmulatorExplorer.UiSmoke.Tests/DailySafetyAuditTests.cs)

| Case | Result | Duration |
| --- | --- | --- |
| Active_delivery_cannot_be_deleted_through_dlq_command | Passed | 00:00:04.9967403 |
| Active_delivery_cannot_be_replayed_and_broker_is_unchanged | Passed | 00:00:04.9870297 |
| Concurrent_replay_requests_reserve_distinct_attempts_and_keep_original | Passed | 00:00:09.4096776 |
| Delete_canceled_before_start_settles_nothing | Passed | 00:00:05.2765788 |
| Delete_of_previous_connection_delivery_is_rejected_after_reconnect | Passed | 00:00:07.7940576 |
| Disconnected_replay_cannot_send_using_old_credentials | Passed | 00:00:05.0834165 |
| Empty_delete_is_a_noop_without_clearing_loaded_delivery | Passed | 00:00:05.2686354 |
| Failed_replay_reservation_persistence_prevents_broker_send | Passed | 00:00:05.0430445 |
| Invalid_edited_json_blocks_replay_selection_and_discard_restores_eligibility | Passed | 00:00:05.2362342 |
| Replay_canceled_before_start_sends_nothing_and_keeps_original | Passed | 00:00:05.2868265 |
| Replay_of_previous_connection_delivery_is_rejected_after_reconnect | Passed | 00:00:08.1913539 |
| Valid_edited_json_replays_changed_copy_and_preserves_original_bytes | Passed | 00:00:07.3419773 |

### [DailySearchLifecycleAuditTests](../tests/ServiceBusEmulatorExplorer.UiSmoke.Tests/DailySearchLifecycleAuditTests.cs)

| Case | Result | Duration |
| --- | --- | --- |
| Browse_active_page_and_inspect_json_body | Passed | 00:00:05.4677523 |
| Clear_search_restores_browse_scope_and_selection | Passed | 00:00:06.2566640 |
| Dirty_inspector_draft_blocks_profile_switch_until_discard_is_confirmed | Passed | 00:00:05.6966317 |
| Disconnect_clears_search_and_reconnect_starts_a_clean_browse_session | Passed | 00:00:08.6897293 |
| Duplicate_message_ids_remain_distinct_deliveries | Passed | 00:00:05.6348415 |
| Failed_connection_can_recover_by_switching_to_a_valid_profile | Passed | 00:00:05.9902412 |
| Inspector_preserves_invalid_json_and_unicode_text | Passed | 00:00:17.4418702 |
| Inspector_shows_non_utf8_body_as_base64_without_data_loss | Passed | 00:00:05.6073854 |
| Invalid_query_is_reported_without_returning_rows | Passed | 00:00:03.5330013 |
| Preferences_round_trip_updates_search_budget_and_timestamp_display | Passed | 00:00:03.3301801 |
| Profile_warning_denial_preserves_session_and_approved_auto_connect_switches_profile | Passed | 00:00:06.0943051 |
| Search_correlation_id_unions_queue_and_topic_subscription_deliveries | Passed | 00:00:07.4766526 |
| Search_exact_message_id_returns_the_expected_delivery | Passed | 00:00:06.1982096 |
| Search_no_match_completes_without_stale_rows | Passed | 00:00:06.1135592 |
| Search_scope_filters_queue_topic_subscription_and_clear_restores_all_matches | Passed | 00:00:07.3900915 |
| Search_stop_during_discovery_can_continue_into_message_scanning | Passed | 00:00:21.2374665 |
| Unqualified_message_id_OR_search_ignores_correlation_only_matches | Passed | 00:00:05.9745484 |

### [DailyWindowAuditTests](../tests/ServiceBusEmulatorExplorer.UiSmoke.Tests/DailyWindowAuditTests.cs)

| Case | Result | Duration |
| --- | --- | --- |
| Automatic_refresh_observes_external_arrival_without_clicking_refresh | Passed | 00:00:14.6271577 |
| Closing_and_reopening_restores_connection_selected_queue_and_fresh_messages | Passed | 00:00:14.6159221 |
| Dead_letter_navigation_does_not_present_zero_total_beside_observed_dead_letters | Failed | 00:00:08.4975327 |
| Empty_queue_then_external_send_and_manual_refresh_loads_without_consuming | Passed | 00:00:09.0128970 |
| Manual_refresh_does_not_present_zero_active_total_beside_observed_active_messages | Failed | 00:00:09.1765541 |
| Refresh_preserves_user_focused_message_after_external_arrival | Passed | 00:00:09.4676318 |

## Local raw evidence

The following generated files are local artifacts, not committed test fixtures:
- [new-window.trx](../artifacts/daily-audit/results/new-window.trx)
- [new-workflows.trx](../artifacts/daily-audit/results/new-workflows.trx)
- [workflows-final.trx](../artifacts/daily-audit/results/workflows-final.trx)
- [core-final.trx](../artifacts/daily-audit/results/core-final.trx)

Supplementary existing cases: 15 broker cases and 4 current-shell real-window journeys passed. They are excluded from the 66 new-case count.
