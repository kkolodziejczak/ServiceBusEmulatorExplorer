# ServiceBusEmulatorExplorer

ServiceBusEmulatorExplorer is a WPF desktop app for inspecting Azure Service Bus through the Azure SDK, with both local-emulator/SAS and Azure CLI/RBAC connection modes.

## Azure CLI / RBAC

The app can also connect to an Azure public-cloud Service Bus namespace with the account already authenticated by Azure CLI. Install Azure CLI, run `az login`, then choose **AzureCli** in the app and enter only the fully qualified namespace, such as `orders.servicebus.windows.net`. The app never launches login itself and never stores Azure CLI access tokens or its credential cache.

Azure Service Bus remains the authorization authority; the app does not inspect role assignments. The supported Azure CLI/RBAC contract is **Azure Service Bus Data Owner assigned at the whole namespace scope**:

| Required role and scope | Browse topology | Peek queue/subscription | Send queue/topic |
| --- | --- | --- | --- |
| Data Owner — namespace | Yes | Yes | Yes |

Sender-only, Receiver-only, combined Sender+Receiver, and entity-scoped-only assignments are not supported workflows for this explorer. Insufficient or wrongly scoped access is shown as an authorization error without disconnecting the current session.

Topics are send destinations. To read messages sent to a topic, select one of its subscriptions and peek there; messages are never read directly from a topic.

Azure RBAC entity management is out of scope. In Azure CLI mode, create, update, and delete entity actions are disabled with an explanation. Use a connection-string profile for the existing emulator/SAS entity-management workflow. Send and peek actions remain enabled according to the current selection and connection, not an assumed role.

Troubleshooting: run `az login` if the CLI is unavailable or the session has expired; use `az account show` to inspect the active account and `az login --tenant <tenant-id>` when the wrong tenant is selected. New role assignments can take several minutes to propagate. Authorization failures retain the service detail in the operation log and keep the session connected so another permitted operation can still be attempted.

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

### Opt-in Azure RBAC proof

The real-Azure proof is skipped by default and never creates, updates, or deletes entities or settles/deletes messages. It uses pre-provisioned topic and subscription inputs, peeks at most one subscription message, then sends one uniquely tagged, non-sensitive text message to the topic:

```powershell
$env:SBE_RUN_AZURE_RBAC_TESTS = "true"
$env:SBE_AZURE_NAMESPACE = "orders.servicebus.windows.net"
$env:SBE_AZURE_TOPIC = "pre-provisioned-topic"
$env:SBE_AZURE_SUBSCRIPTION = "pre-provisioned-subscription"
dotnet test tests\ServiceBusEmulatorExplorer.Integration.Tests\ServiceBusEmulatorExplorer.Integration.Tests.csproj --filter TestCategory=AzureRbac
```

Run this with an Azure CLI account assigned **Azure Service Bus Data Owner at the namespace scope**. Do not treat a skipped cloud proof as success. Current repository environment result (2026-07-21): not executed because `az` is unavailable and `SBE_RUN_AZURE_RBAC_TESTS`, `SBE_AZURE_NAMESPACE`, `SBE_AZURE_TOPIC`, and `SBE_AZURE_SUBSCRIPTION` are unset.

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
