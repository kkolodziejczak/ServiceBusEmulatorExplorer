# ServiceBusEmulatorExplorer

ServiceBusEmulatorExplorer is a WPF desktop app for inspecting and controlling a local Azure Service Bus emulator directly through the Azure SDK.

## Local Service Bus emulator

Copy the sample environment file and start the emulator:

```powershell
Copy-Item .env.example .env
docker compose --env-file .env up -d
```

Host connection strings for the WPF app, integration tests, and UI smoke tests:

```text
Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;
Endpoint=sb://localhost:5300;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;
```

The first string is the runtime connection string. The second string is the administration connection string.

## Quickstart

1. Start the emulator with `docker compose --env-file .env up -d`.
2. Run `dotnet build`.
3. Start `src\ServiceBusEmulatorExplorer.App` from Visual Studio or with `dotnet run --project src\ServiceBusEmulatorExplorer.App`.
4. Enter the runtime and administration connection strings above.
5. Create a queue or topic, send a message, peek active/DLQ messages, and use replay/delete DLQ commands explicitly.

## Tests

Fast unit and view-model tests are the default loop:

```powershell
dotnet test --filter "TestCategory!=Integration&TestCategory!=UiSmoke"
```

Integration tests are Docker-backed and must be enabled explicitly after the emulator is up:

```powershell
$env:SBE_RUN_INTEGRATION_TESTS = "true"
$env:SBE_CONNECTION_STRING = "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;"
$env:SBE_ADMIN_CONNECTION_STRING = "Endpoint=sb://localhost:5300;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;"
dotnet test --filter TestCategory=Integration
```

UI smoke tests launch the WPF app and are also explicit:

```powershell
dotnet build src\ServiceBusEmulatorExplorer.App\ServiceBusEmulatorExplorer.App.csproj
$env:SBE_RUN_UI_TESTS = "true"
$env:SBE_APP_EXE = "$PWD\src\ServiceBusEmulatorExplorer.App\bin\Debug\net10.0-windows\ServiceBusEmulatorExplorer.App.exe"
.\scripts\Invoke-UiSmoke.ps1 -NoBuild
```

The UI smoke runner checks progress every 15 seconds and stops the test process plus WPF app instances it launched after 60 seconds by default. Its default is the launch smoke test. To run the full UI smoke suite explicitly:

```powershell
.\scripts\Invoke-UiSmoke.ps1 -NoBuild -FullSuite
```

Navigation and DLQ UI smoke tests also require the emulator connection strings shown above.

## Manual proof

Run the scripted local proof from the repository root:

```powershell
.\scripts\Invoke-ManualProof.ps1
```

The script starts the compose emulator, runs the gated integration proof, publishes the app, and prints the WPF checklist for confirming the DLQ workflow manually. The key manual result is that replaying a DLQ copy creates a new active message while the original DLQ message remains until the separate delete command is confirmed.

## Packaging

Publish a self-contained Windows artifact:

```powershell
.\scripts\Publish-Windows.ps1
```

The default output is `artifacts\publish\win-x64`, which is ignored by git.

Stop the emulator:

```powershell
docker compose down
```
