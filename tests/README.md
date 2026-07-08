# Service Bus Emulator Explorer Tests

This directory is reserved for the test projects created in Stage 1.

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
dotnet test
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

WPF UI smoke tests:

```powershell
$env:SBE_RUN_UI_TESTS = "true"
dotnet test --filter TestCategory=UiSmoke
```

## First UI Smoke Test

The first FlaUI/UIA3 smoke test should:

1. Launch the WPF app executable.
2. Find the main window.
3. Verify the window title is `Service Bus Emulator Explorer`.
4. Verify `Connect`, `Disconnect`, and `Refresh` are discoverable controls.
5. Verify no startup exception dialog is shown.
6. Close the app cleanly.

Keep UI smoke tests small and focused. Broader behavior should stay in unit and integration tests where it is faster and less fragile.
