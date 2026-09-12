# Service Bus Emulator Explorer Tests

This directory contains the fast unit, Docker-backed integration, and explicit WPF UI smoke test projects for the MVP.

## Test Layers

| Project | Purpose | Default run |
| --- | --- | --- |
| `ServiceBusEmulatorExplorer.Core.Tests` | Domain models, projection helpers, replay ID policy, SDK-independent service behavior. | Yes |
| `ServiceBusEmulatorExplorer.App.Tests` | View model command states, validation, UTC operation log formatting, shell state transitions with fakes. | Yes |
| `ServiceBusEmulatorExplorer.Integration.Tests` | Direct Azure SDK behavior against the local Service Bus emulator from `compose.yaml`. | No, gated |
| `ServiceBusEmulatorExplorer.UiSmoke.Tests` | FlaUI/UIA3 smoke tests that launch the WPF app and verify the rendered shell. | No, gated |

## Commands

Fast tests:

```powershell
dotnet test --filter "TestCategory!=Integration&TestCategory!=UiSmoke"
```

Integration tests:

```powershell
Copy-Item .env.example .env
docker compose --env-file .env up -d

$env:SBE_RUN_INTEGRATION_TESTS = "true"
$env:SBE_CONNECTION_STRING = "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;"
$env:SBE_ADMIN_CONNECTION_STRING = "Endpoint=sb://localhost:5300;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;"
dotnet test --filter TestCategory=Integration
```

The focused investigation leaf proof in [InvestigationPeekIntegrationTests.cs](ServiceBusEmulatorExplorer.Integration.Tests/InvestigationPeekIntegrationTests.cs) uses unique queue/topic fixtures, 62 messages, pages of 50, Active/DLQ peeking, cancellation and binary payload preservation. With the integration environment variables above set, run:

```powershell
dotnet test tests/ServiceBusEmulatorExplorer.Integration.Tests/ServiceBusEmulatorExplorer.Integration.Tests.csproj --filter FullyQualifiedName~InvestigationPeekIntegrationTests -m:1 -nr:false --logger "console;verbosity=detailed"
```

Run from the repository root against an isolated emulator. The proof deletes only its generated entities. Runtime counts are logged as observations because the emulator can report zero while messages remain readable. This verifies leaf SDK behavior; it does not verify the investigation workspace UI or combined topic paging.

WPF UI smoke tests:

```powershell
dotnet build ..\src\ServiceBusEmulatorExplorer.App\ServiceBusEmulatorExplorer.App.csproj
$env:SBE_RUN_UI_TESTS = "true"
$env:SBE_APP_EXE = "$PWD\..\src\ServiceBusEmulatorExplorer.App\bin\Debug\net10.0-windows\ServiceBusEmulatorExplorer.App.exe"
..\scripts\Invoke-UiSmoke.ps1 -NoBuild
```

The script checks progress every 15 seconds and stops the test process plus WPF app instances it launched after 60 seconds by default. Its default filter runs the launch smoke test. To run the full UI smoke suite explicitly, use:

```powershell
..\scripts\Invoke-UiSmoke.ps1 -NoBuild -FullSuite
```

Navigation and DLQ UI smoke tests also require `SBE_CONNECTION_STRING` or `SBE_RUNTIME_CONNECTION_STRING`, plus `SBE_ADMIN_CONNECTION_STRING`.

Azure RBAC proof tests are a separate, opt-in E2E layer. They require Azure CLI to be installed and signed in, plus pre-provisioned inputs; they do not create/update/delete resources or settle/delete messages:

```powershell
$env:SBE_RUN_AZURE_RBAC_TESTS = "true"
$env:SBE_AZURE_NAMESPACE = "orders.servicebus.windows.net"
$env:SBE_AZURE_TOPIC = "pre-provisioned-topic"
$env:SBE_AZURE_SUBSCRIPTION = "pre-provisioned-subscription"
dotnet test ServiceBusEmulatorExplorer.Integration.Tests\ServiceBusEmulatorExplorer.Integration.Tests.csproj --filter TestCategory=AzureRbac
```

The proof browses the configured topic/subscription, peeks at most one subscription message, and sends one uniquely tagged non-sensitive text message. It is skipped unless explicitly enabled, so Azure credentials are never a normal-test prerequisite. Run it with **Azure Service Bus Data Owner at namespace scope**; Sender-only, Receiver-only, combined, and entity-scoped-only role configurations are not supported proof targets.

The proof uses the app's namespace topology workflow, which requires the supported Data Owner namespace assignment.

Manual local proof:

```powershell
..\scripts\Invoke-ManualProof.ps1
```

Packaging proof:

```powershell
..\scripts\Publish-Windows.ps1
```

## Investigation workspace checks

From the repository root, run the focused production-workspace tests:

```powershell
dotnet test tests/ServiceBusEmulatorExplorer.App.Tests/ServiceBusEmulatorExplorer.App.Tests.csproj --filter "FullyQualifiedName~Investigation" -m:1 -nr:false -p:UseSharedCompilation=false
```

The WPF render cases use synthetic broker services and exercise the real windows at supported sizes. Set `SBE_CAPTURE_INVESTIGATION_UI=true` to save ignored PNG evidence under `artifacts/investigation-ui`. They do not replace broker or FlaUI end-to-end proof. The protected legacy-profile migration uses Windows atomic file replacement; a restricted sandbox can deny that operation even when ordinary temporary-file writes succeed.

`InvestigationSearchRenderTests` exercises the real search controls for partial results, Continue, Stop, match-tree scope, Clear and draft restoration. `InvestigationGlobalSearchTests` covers discovery limits, stale session results and retrying failed sources. Search suggestions use only previously loaded message observations; their tests do not claim a complete namespace index.

WPF presentation tests share a non-parallel xUnit collection because resource initialization and keyboard focus are process-wide state. Keep new rendered-window and compiled-resource tests in that collection.

With the integration environment configured as above, `--filter "FullyQualifiedName~InvestigationLeafReadIntegrationTests"` on the integration project runs discovery, combined paging, repeated non-consuming reads and bounded-search continuation against isolated, uniquely named broker entities. The case deletes those entities afterward and never purges existing entities.

With `SBE_RUN_UI_TESTS=true` and the same broker environment, the current investigation workflow can also be exercised through the actual app executable. From the repository root, after building the UI smoke project:

```powershell
.\scripts\Invoke-UiSmoke.ps1 -NoBuild -Filter "FullyQualifiedName~InvestigationReadOnlyEndToEndTests" -TimeoutSeconds 180
```

This creates unique queue/topic fixtures and an isolated protected profile, verifies 25/50/52-row paging, DLQ inspection, related-message search across two subscriptions, Clear and normal shutdown, then compares complete broker peek observations before and after. The generated entities and profile are cleaned up. The older navigation/administration/mutation cases in `MainWindowSmokeTests` still target the previous shell and need migration; this focused pass does not establish a full-suite pass.

## First UI Smoke Test

The first FlaUI/UIA3 smoke test should:

1. Launch the WPF app executable.
2. Find the main window.
3. Verify the window title is `Service Bus Emulator Explorer`.
4. Verify the connection selector, connection action, search, Refresh and Settings controls are discoverable.
5. Open Settings and verify queue/topic/subscription page sizes and Azure CLI fields through UI Automation.
6. Disable close-to-tray, verify clean window-driven shutdown, restart with the same isolated profile and verify that Close still exits. Protected preference saving may require running outside a restrictive filesystem sandbox; do not weaken atomic persistence for the test.
6. Verify no startup exception dialog is shown and clean up the isolated app/profile. The broker-backed investigation case separately verifies normal shutdown.

Keep UI smoke tests small and focused. Broader behavior should stay in unit and integration tests where it is faster and less fragile.
