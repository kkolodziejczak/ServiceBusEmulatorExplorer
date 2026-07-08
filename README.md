# ServiceBusEmulatorExplorer

## Local Service Bus emulator

Copy the sample environment file and start the emulator:

```powershell
Copy-Item .env.example .env
docker compose --env-file .env up -d
```

Host connection strings for the WPF app and integration tests:

```text
Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;
Endpoint=sb://localhost:5300;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;
```

Run integration tests explicitly after the emulator is up:

```powershell
$env:SBE_RUN_INTEGRATION_TESTS = "true"
$env:SBE_CONNECTION_STRING = "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;"
$env:SBE_ADMIN_CONNECTION_STRING = "Endpoint=sb://localhost:5300;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;"
dotnet test --filter TestCategory=Integration
```

Stop the emulator:

```powershell
docker compose down
```

## Tests

The MVP plan starts test projects in Stage 1. See [tests/README.md](tests/README.md) for the intended unit, integration, and WPF UI smoke test layers.
