# Capabilities index

What has already been solved here and where to find it. Before designing a technique, look it
up and read the referenced code. Pointers name the file and the type or method, never a line
number. Every row says how to reuse it:

- **shared**: call the shared implementation; do not copy it.
- **adapt**: copy and adapt locally; the pointer is the canonical example. Copies get no fixes.
- **example**: a reference only; the technique is specific to its owner.

Keep this file current: when a change adds a technique another screen or layer could reuse, add
a row in the same change. When code moves, update the pointer. `tools/check-docs.ps1` fails on
dead paths.

## Modules at a glance

| Module | What it is | Depends on |
| --- | --- | --- |
| `src/ServiceBusEmulatorExplorer.Core/` | Service Bus SDK access, entity discovery, message projection, replay and delete services, watch and search queries | Azure SDK |
| `src/ServiceBusEmulatorExplorer.App/` | WPF shell: Investigation workspace, Message Workbench prototype, dialogs, shared styles, settings | Core |
| `tests/` | Core and App unit tests (default), Docker-gated integration tests, gated FlaUI UI smoke tests | — |
| `tools/ServiceBusEmulatorExplorer.ReadmeScreenshot/` | Headless screenshot harness for proof images | App |
| `tools/ServiceBusEmulatorExplorer.InvestigationPrototype/` | The approved prototype; historical reference, not production | — |
| `scripts/` | Bounded runners for UI smoke, manual proof, publish and release tagging | — |

## UI: styles and controls

Startup presentation example: `src/ServiceBusEmulatorExplorer.App/Investigation/InvestigationWindow.xaml.cs`, `WindowLoaded`, renders empty surfaces before asynchronous initialization. `InvestigationWindow.Search.cs`, `UpdateNamespaceEmptyState`, distinguishes discovery, empty connections and filters. Delayed-startup proof lives in `tests/ServiceBusEmulatorExplorer.App.Tests/InvestigationStartupRenderTests.cs`.

| Capability | Reuse | Where |
| --- | --- | --- |
| Shared brushes, button variants, tab toggle, dialog text styles, grid styles, scrollbars | shared | `src/ServiceBusEmulatorExplorer.App/Investigation/Resources/SharedStyles.xaml` (see [DESIGN.md](DESIGN.md) for the registry) |
| Dropdown with compact and table-editing variants | shared | `src/ServiceBusEmulatorExplorer.App/Investigation/Resources/DropdownStyles.xaml` (`AppComboBox`, `CompactComboBox`) |
| Calendar-only date selection with release-to-open pointer capture, keyboard opening and read-only UI Automation value | shared | `src/ServiceBusEmulatorExplorer.App/Investigation/CalendarOnlyDatePicker.cs` (`CalendarOnlyDatePicker`); use with the shared `ScheduleDatePicker` style |
| Vector icon geometries | shared | `src/ServiceBusEmulatorExplorer.App/Investigation/Resources/IconResources.xaml` |
| Profile accent recolouring of the whole brush set at runtime | shared | `src/ServiceBusEmulatorExplorer.App/Investigation/Resources/ProfileTheme.cs` `Apply` |
| Keyboard-only focus cue (no frame after pointer click) | shared | `SharedStyles.xaml` `KeyboardActionFocusVisual`, used by the implicit `Button` style |
| Validation frame with a remedy action that navigates and focuses the field | adapt | `src/ServiceBusEmulatorExplorer.App/Investigation/MessageLibraryPrototypeView.xaml` (`ValidationWarningFrame` usage) and its code-behind partials |
| Responsive editor with retained draft, inspector choice and focused surface | adapt | `src/ServiceBusEmulatorExplorer.App/Investigation/MessageLibraryPrototypeView.Layout.cs`, `ApplyEditorResponsiveLayout`; same-window resize proof in `tests/ServiceBusEmulatorExplorer.App.Tests/MessageWorkbenchResponsiveComposeTests.cs` |
| JSON syntax colouring on the dark editor | shared | `src/ServiceBusEmulatorExplorer.App/Investigation/Inspection/JsonSyntaxColorizer.cs` |
| Workbench search suggestions with scope-specific navigation | adapt | `src/ServiceBusEmulatorExplorer.App/Investigation/InvestigationWindow.Search.cs` (`UpdateSuggestions`, `ChooseSuggestion`) and `src/ServiceBusEmulatorExplorer.App/Investigation/MessageLibraryPrototypeView.Search.cs` |
| Inspector text search with match navigation (Ctrl+F) | adapt | `src/ServiceBusEmulatorExplorer.App/Investigation/InvestigationWindow.Search.cs` |
| Large window split into partial classes by concern | adapt | `src/ServiceBusEmulatorExplorer.App/Investigation/InvestigationWindow.*.cs` (Delete, Inspection, Layout, Lifetime, Notifications, Replay, Search, Watch) |
| Tray icon and toast-style notification window | adapt | `src/ServiceBusEmulatorExplorer.App/Investigation/InvestigationTray.cs`; `WatchNotificationWindow.cs` |

## Architecture and workflows

| Capability | Reuse | Where |
| --- | --- | --- |
| Shell view model as orchestration with extracted feature workflows | adapt | `src/ServiceBusEmulatorExplorer.App/ViewModels/ShellViewModel.cs`; `src/ServiceBusEmulatorExplorer.App/Services/EntityManagementWorkflow.cs`; `src/ServiceBusEmulatorExplorer.App/ViewModels/MessageInspectionViewModel.cs` |
| Dialogs as input collectors behind interfaces (testable without WPF) | adapt | `src/ServiceBusEmulatorExplorer.App/Services/IEntityManagementDialogService.cs`, `IMessageDialogService.cs` and their `Wpf*` implementations |
| Operation failure text without leaking SDK internals | shared | `src/ServiceBusEmulatorExplorer.App/Services/OperationFailureFormatter.cs` |
| Injected clock for UTC operation-log timestamps | shared | `src/ServiceBusEmulatorExplorer.App/Services/IClock.cs`, `SystemClock.cs` |
| Protected per-user preferences store with an overridable path for tests | shared | `src/ServiceBusEmulatorExplorer.App/Investigation/Settings/ProtectedWorkspacePreferencesStore.cs` |
| Entity tree built from administration listings | shared | `src/ServiceBusEmulatorExplorer.Core/ServiceBus/EntityTreeBuilder.cs` |
| Non-destructive DLQ replay (new message, original left in DLQ) | shared | `src/ServiceBusEmulatorExplorer.Core/ServiceBus/DeadLetterReplayService.cs`, `ReplayCopySender.cs`, `DeadLetterReplayRequestFactory.cs` |
| Exact reviewed replay ID/body with durable pre-send attempt history | adapt | `src/ServiceBusEmulatorExplorer.App/Investigation/InvestigationWorkspace.Replay.cs`, `PrepareReplays`, `SendReplayAsync`; `PreparedReplay.cs`; `src/ServiceBusEmulatorExplorer.App/Investigation/Settings/ProtectedWorkspacePreferencesStore.ReplayHistory.cs` |
| Bounded complete Active/DLQ observations for exact replay IDs | shared | `src/ServiceBusEmulatorExplorer.Core/Investigation/ReplayObserver.cs`, `ObserveAsync`; `ReplayObservation.cs` |
| Modeless replay history with main-window exact-ID filtering | adapt | `src/ServiceBusEmulatorExplorer.App/Investigation/ReplayHistoryWindow.xaml.cs`; `InvestigationWindow.ReplayHistory.cs`, `FindReplayIdsAsync` |
| Selected-row inline history details with full-ID copying and row-owned actions | adapt | `src/ServiceBusEmulatorExplorer.App/Investigation/ReplayHistoryWindow.xaml`, DataGrid row details; `ReplayHistoryWindow.xaml.cs`, `CopyId`, `DeleteOriginal_Click`; `ReplayHistoryRow.cs`, presentation projection |
| Replay history visual hierarchy and adjacent wrapping identity actions | adapt | `src/ServiceBusEmulatorExplorer.App/Investigation/Resources/SharedStyles.xaml`, `ReplayHistoryGrid`, `ReplayHistoryBadge`, `ReplayHistoryDetailId`; `ReplayHistoryWindow.xaml`, row-details WrapPanel |
| Durable local-history hiding without losing replay safety lineage | adapt | `src/ServiceBusEmulatorExplorer.App/Investigation/InvestigationWorkspace.ReplayHistory.cs`, `CanRemoveReplayHistory`, `RemoveReplayHistoryAsync`, `CurrentReplayAttempts`; visible history is a separate projection |
| Scope header and address-based tree selection restoration | adapt | `src/ServiceBusEmulatorExplorer.App/Investigation/InvestigationWindow.Scope.cs`, `UpdateBrowseScopeHeader`, `RestoreNamespaceSelection`; restore only the selected scope's ancestor path |
| Fresh replay absence checks before captured-original DLQ cleanup | adapt | `src/ServiceBusEmulatorExplorer.App/Investigation/InvestigationWorkspace.ReplayOriginalCleanup.cs`, `PrepareReplayCleanupAsync`, `DeleteReplayOriginalAsync` |
| JSON object/array folding without document edits | shared | `src/ServiceBusEmulatorExplorer.App/Investigation/Inspection/JsonEditor.cs`, `SetInspectionDocument`, `RevealRange`; `JsonFoldRanges.cs` validates and locates UTF-16 ranges |
| Themed folding gutter and instance-scoped collapsed placeholders | shared | `src/ServiceBusEmulatorExplorer.App/Investigation/Inspection/JsonFoldingMargin.cs`; `JsonFoldPlaceholder.cs`; `JsonEditor.cs`, `AttachFolds`, `DetachFolds` |
| Exact saved replay labels on live message rows | adapt | `src/ServiceBusEmulatorExplorer.App/Investigation/InvestigationWorkspace.ReplayLabels.cs`, `UpdateReplayLabels`; `WorkspaceRows.cs`, `MessageRow.SetReplay` |
| DLQ delete scoped to captured deliveries with sequence-number safety | shared | `src/ServiceBusEmulatorExplorer.Core/ServiceBus/DlqDeleteReceiver.cs`; `src/ServiceBusEmulatorExplorer.Core/Investigation/DlqDeliveryDeleter.cs` |
| Watch scope resolution with inherited and overridden inclusion | shared | `src/ServiceBusEmulatorExplorer.Core/Investigation/WatchScopeResolver.cs` |
| Namespace Watch and current-view refresh presentation | adapt | `src/ServiceBusEmulatorExplorer.App/Investigation/InvestigationWindow.Indicators.cs`, `UpdateNamespaceIndicators`; `src/ServiceBusEmulatorExplorer.App/Investigation/EntityNode.Indicators.cs` |
| Background watched-view snapshots with independent paging | adapt | `src/ServiceBusEmulatorExplorer.App/Investigation/MessageBrowseWorkflow.Background.cs`, `RefreshBackgroundAsync`, `SetWatchRules`; `src/ServiceBusEmulatorExplorer.App/Investigation/MessageBrowseWorkflow.cs`, `SelectAsync` |
| Message search query model | shared | `src/ServiceBusEmulatorExplorer.Core/Investigation/MessageSearchQuery.cs` |

## Testing, proof and delivery

| Capability | Reuse | Where |
| --- | --- | --- |
| Fast test loop excluding gated categories | shared | `AGENTS.md` (Fast Development Loop); `tests/README.md` |
| Docker-gated integration tests against the local emulator | adapt | `tests/ServiceBusEmulatorExplorer.Integration.Tests/`; `compose.yaml` |
| FlaUI UI smoke tests using UI Automation patterns, not physical input | adapt | `tests/ServiceBusEmulatorExplorer.UiSmoke.Tests/`; runner `scripts/Invoke-UiSmoke.ps1` |
| Headless screenshot of the real window with synthetic services | shared | `tools/ServiceBusEmulatorExplorer.ReadmeScreenshot/Program.cs`; `WpfScreenshot.cs` |
| Exact WPF test viewport beyond a hosted desktop size limit | shared | `tools/ServiceBusEmulatorExplorer.ReadmeScreenshot/NativeWindowSizeOverride.cs`, `NativeWindowSizeOverride.Install`; linked into App.Tests through its project file; dispose the window hook after proof |
| Guarded release tagging and two-flavour Windows publish | shared | `scripts/Push-ReleaseTag.ps1`, `scripts/Publish-Windows.ps1`, `scripts/Test-ReleaseArtifacts.ps1` |
| Convention check with ratchet baseline | shared | `tools/check-docs.ps1`; config `.agent-kit.json` |

Scheduling controls: reuse [SchedulingStyles.xaml](src/ServiceBusEmulatorExplorer.App/Investigation/Resources/SchedulingStyles.xaml) for the date picker, calendar and aligned radio choices. [Scheduling contract](specs/message-library/scheduling-controls.md).

App-wide date display: shared `DateDisplay` and inheritable `DatePresentation` in [DateDisplay.cs](src/ServiceBusEmulatorExplorer.App/Investigation/DateDisplay.cs) format UI values without changing payloads or instants. [Contract](specs/date-display.md).

Inspector action alignment: `InspectorToolbar_SizeChanged` in [InvestigationWindow.Layout.cs](src/ServiceBusEmulatorExplorer.App/Investigation/InvestigationWindow.Layout.cs) measures the two groups and wraps the right-aligned actions only when they cannot fit alongside the tabs.

## Compact delivery presentation

| Capability | Reuse | Where |
| --- | --- | --- |
| Topic/subscription location hierarchy with queue fallback | shared | `src/ServiceBusEmulatorExplorer.App/Investigation/Resources/SharedStyles.xaml` (`MessageLocationTemplate`, `MessageLocationIcon`, `MessageLocationText`); `src/ServiceBusEmulatorExplorer.App/Investigation/WorkspaceRows.cs` (`MessageRow.SourceDetail`) |
| Distinct replay IDs versus per-entity delivery counts | shared | `src/ServiceBusEmulatorExplorer.App/Investigation/ReplayDeliverySummary.cs` (`Format`); `src/ServiceBusEmulatorExplorer.App/Investigation/InvestigationWindow.ReplayHistory.cs` (`FindReplayIdsAsync`) |
| Global typed message-column catalog and saved choices | adapt | `src/ServiceBusEmulatorExplorer.App/Investigation/MessageColumnCatalog.cs` (`Normalize`, `Discover`, `Value`, `Format`, `Compare`); `src/ServiceBusEmulatorExplorer.App/Investigation/InvestigationWindow.Columns.cs` (`UpdateMessageColumns`); `src/ServiceBusEmulatorExplorer.App/Investigation/InvestigationWorkspace.cs` (`UpdateMessageColumnsAsync`) |
| Inspector permission status without edit-mode gating | example | `src/ServiceBusEmulatorExplorer.App/Investigation/InvestigationWindow.Inspection.cs` (`UpdateInspector`); `src/ServiceBusEmulatorExplorer.App/Investigation/Resources/SharedStyles.xaml` (`InspectorStatusText`) |
| Viewport-anchored hover/focus Copy | shared | `src/ServiceBusEmulatorExplorer.App/Investigation/InvestigationWindow.InspectorCopy.cs` (`UpdateInspectorCopy`); `src/ServiceBusEmulatorExplorer.App/Investigation/InvestigationWindow.Layout.cs` (`UpdateLayoutMode`) |

| Shared persisted column order independent of visibility | adapt | `src/ServiceBusEmulatorExplorer.App/Investigation/MessageColumnCatalog.cs` (`NormalizeOrder`, `ReorderVisible`); `src/ServiceBusEmulatorExplorer.App/Investigation/InvestigationWindow.ColumnOrder.cs` (`ApplyColumnOrder`, `ColumnMove_Click`) |

| Order-aware message table sizing | adapt | `src/ServiceBusEmulatorExplorer.App/Investigation/InvestigationWindow.Layout.cs` (`FitMessageIdentityColumns`); `src/ServiceBusEmulatorExplorer.App/Investigation/Resources/SharedStyles.xaml` (`InvestigationMessageCell`) |
