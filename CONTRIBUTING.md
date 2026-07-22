# Contributing to Service Bus Emulator Explorer

Thank you for helping improve Service Bus Emulator Explorer. Contributions of bug reports, documentation, tests, design feedback, and code are welcome.

By participating, you agree to follow the [Code of Conduct](CODE_OF_CONDUCT.md). By submitting a contribution, you agree that it may be distributed under the repository's [MIT License](LICENSE).

## Before you start

- Search [existing issues](https://github.com/kkolodziejczak/ServiceBusEmulatorExplorer/issues) before opening a new one.
- Use the bug or feature issue form when it fits your request.
- For a security vulnerability, follow [SECURITY.md](SECURITY.md) instead of opening a public issue.
- For setup and usage questions, see [SUPPORT.md](SUPPORT.md).
- For a substantial change, open an issue first so the behavior and scope can be discussed before implementation.

Small documentation corrections and focused test improvements can usually go directly to a pull request.

## Development setup

The application is Windows-only because it uses WPF.

Required:

- Windows
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Git and PowerShell

Optional, depending on the change:

- Docker Desktop or another Docker Compose-compatible environment for the local emulator and integration tests
- Visual Studio with .NET desktop development support
- Azure CLI and a pre-provisioned test namespace for the explicit Azure RBAC proof

Clone, build, and run:

```powershell
git clone https://github.com/kkolodziejczak/ServiceBusEmulatorExplorer.git
Set-Location ServiceBusEmulatorExplorer
dotnet restore ServiceBusEmulatorExplorer.slnx
dotnet build ServiceBusEmulatorExplorer.slnx
dotnet run --project src\ServiceBusEmulatorExplorer.App
```

## Local emulator

Copy the checked-in example environment file before starting Docker Compose:

```powershell
Copy-Item .env.example .env
docker compose --env-file .env up -d
```

The `.env` file is ignored by Git. Do not replace the sample values with real credentials or commit local secrets.

Stop the emulator when finished:

```powershell
docker compose down
```

## Making a change

1. Create a focused branch from the current default branch.
2. Reproduce the bug or identify the behavior you intend to change.
3. Keep the change limited to one concern where practical.
4. Add or update tests that prove the behavior.
5. Update user or contributor documentation when commands, UI behavior, or constraints change.
6. Run the relevant checks locally.
7. Open a pull request and complete the checklist in the template.

### Code guidelines

- Keep nullable reference types enabled and resolve warnings intentionally.
- Keep Azure SDK and domain behavior in `ServiceBusEmulatorExplorer.Core`; keep WPF orchestration and presentation in the app project.
- Keep `ShellViewModel` focused on orchestration. Put feature workflows and mapping rules in focused classes.
- Preserve cancellation and bounded timeouts around SDK operations.
- Treat Service Bus message operations as message creation or settlement; messages are not updated in place.
- Keep DLQ replay non-destructive by default and keep deletion as a separate, confirmed command.
- Do not add production-only seams solely to make an Azure SDK model testable; prefer Azure SDK model factories.
- Keep operation-log timestamps explicitly in UTC.
- Preserve stable automation IDs and accessible names for UI controls.
- Avoid fixed layouts that clip controls when the WPF window is resized.
- Pin package and GitHub Action versions; do not introduce floating versions.

### Documentation and screenshots

Keep commands runnable from the directory stated in the document. Prefer repository-relative links. Never include connection strings, tokens, real message bodies, customer identifiers, or other sensitive data in examples.

The main README screenshot is generated from a deterministic WPF scenario after a release. Do not replace it with an ad hoc screenshot.

## Testing

Run the smallest relevant test first, then the broader fast suite before opening a pull request.

### Fast suite

This is the default contributor and CI loop:

```powershell
dotnet test ServiceBusEmulatorExplorer.slnx --filter "TestCategory!=Integration&TestCategory!=UiSmoke"
```

### Integration suite

Integration tests use the Docker-backed Service Bus emulator and are opt-in:

```powershell
$env:SBE_RUN_INTEGRATION_TESTS = "true"
$env:SBE_CONNECTION_STRING = "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;"
$env:SBE_ADMIN_CONNECTION_STRING = "Endpoint=sb://localhost:5300;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;"
dotnet test tests\ServiceBusEmulatorExplorer.Integration.Tests\ServiceBusEmulatorExplorer.Integration.Tests.csproj
```

### WPF UI smoke suite

UI smoke tests launch the application. Use the bounded runner rather than invoking the UI test project directly:

```powershell
dotnet build src\ServiceBusEmulatorExplorer.App\ServiceBusEmulatorExplorer.App.csproj
$env:SBE_RUN_UI_TESTS = "true"
$env:SBE_APP_EXE = "$PWD\src\ServiceBusEmulatorExplorer.App\bin\Debug\net10.0-windows\ServiceBusEmulatorExplorer.App.exe"
.\scripts\Invoke-UiSmoke.ps1 -NoBuild
```

The default runner performs launch smoke. Use `-FullSuite` only when the broader UI workflow is relevant. Emulator-backed navigation and DLQ tests also require the runtime and administration connection strings shown above.

### Manual and packaging proof

Changes that affect the end-to-end local workflow or packaging may also require:

```powershell
.\scripts\Invoke-ManualProof.ps1
.\scripts\Publish-Windows.ps1
```

Read a script before running it. Scripts in this repository may start Docker, launch WPF, or publish artifacts. More test detail is available in [tests/README.md](tests/README.md).

## Pull requests

A reviewable pull request should:

- Explain the problem and the chosen behavior.
- Link the relevant issue when one exists.
- Describe how the change was tested, including anything that could not be tested.
- Include screenshots for visible UI changes.
- Call out compatibility, security, data-loss, or operational risks.
- Avoid unrelated formatting or cleanup.
- Leave generated outputs, local profiles, `.env`, logs, and published artifacts out of Git.

Maintainers may ask for a smaller scope, additional proof, or documentation before merging. A pull request is not guaranteed to be accepted, but feedback should explain the relevant technical or project constraint.

## Release work

Release tags and GitHub Releases are handled by maintainers. Contributors do not need to prepare or push tags as part of a pull request.
