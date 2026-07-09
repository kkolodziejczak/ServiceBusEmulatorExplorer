# ServiceBusEmulatorExplorer Agent Notes

These notes are repo-specific guidance gathered from prior planning, implementation, verification, and bug-fix sessions. Treat them as operating guidance for this repository; still inspect the current code and scripts before acting.

## Project Shape

- Main solution: `ServiceBusEmulatorExplorer.slnx`.
- App: WPF desktop app in `src/ServiceBusEmulatorExplorer.App`.
- Domain/SDK layer: `src/ServiceBusEmulatorExplorer.Core`.
- Test layers:
  - fast unit/view-model tests in `tests/ServiceBusEmulatorExplorer.Core.Tests` and `tests/ServiceBusEmulatorExplorer.App.Tests`;
  - Docker-backed Service Bus emulator tests in `tests/ServiceBusEmulatorExplorer.Integration.Tests`;
  - explicit FlaUI/UIA3 WPF smoke tests in `tests/ServiceBusEmulatorExplorer.UiSmoke.Tests`.
- `Lessons/` has been used for planning notes and may be untracked. Do not stage or rewrite it unless the task explicitly asks for lesson work.

## Command Discipline

- Estimate runtime before every command and set an explicit timeout.
- Normal build/test commands should usually have a 60 second cap. Use a larger cap only when the command itself is deliberately longer, such as Docker emulator readiness, full UI smoke, manual proof, or publish.
- After 3 failed attempts, stop retrying and inspect the cause: inputs, command quoting, environment variables, process state, logs, and script behavior.
- If a command fails without useful output, narrow it before retrying. For tests, prefer project-level or single-test commands over repeating solution-wide commands.
- Before running scripts in `scripts/`, read the script first. They are intended to be bounded, but they may start Docker, launch WPF, or publish artifacts.
- PowerShell quoting is easy to get wrong here. If `rg` lookahead or filter expressions with `|` fail because of shell parsing, change the command shape instead of retrying the same syntax.

## Fast Development Loop

- Default fast loop:

```powershell
dotnet test ServiceBusEmulatorExplorer.slnx --filter "TestCategory!=Integration&TestCategory!=UiSmoke"
```

- `dotnet test ServiceBusEmulatorExplorer.slnx` should stay green with integration and UI smoke tests skipped by default.
- Use `git diff --check` before staging. Line-ending normalization warnings may appear; distinguish those from actual whitespace errors.
- Package versions were pinned after a quality review found floating dependencies risky. Avoid reintroducing floating package versions.

## Local Emulator

- Start the emulator with:

```powershell
Copy-Item .env.example .env
docker compose --env-file .env up -d
```

- Runtime connection string:

```text
Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;
```

- Administration connection string:

```text
Endpoint=sb://localhost:5300;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;
```

- Integration tests are opt-in:

```powershell
$env:SBE_RUN_INTEGRATION_TESTS = "true"
$env:SBE_CONNECTION_STRING = "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;"
$env:SBE_ADMIN_CONNECTION_STRING = "Endpoint=sb://localhost:5300;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;"
dotnet test tests\ServiceBusEmulatorExplorer.Integration.Tests\ServiceBusEmulatorExplorer.Integration.Tests.csproj
```

- If solution-wide integration execution hangs or gives no output, run the integration test project directly with hang diagnostics rather than repeating the solution command.

## WPF UI Smoke Tests

- UI smoke tests are opt-in and launch the WPF app:

```powershell
dotnet build src\ServiceBusEmulatorExplorer.App\ServiceBusEmulatorExplorer.App.csproj
$env:SBE_RUN_UI_TESTS = "true"
$env:SBE_APP_EXE = "$PWD\src\ServiceBusEmulatorExplorer.App\bin\Debug\net10.0-windows\ServiceBusEmulatorExplorer.App.exe"
.\scripts\Invoke-UiSmoke.ps1 -NoBuild
```

- The UI smoke runner defaults to launch smoke only. Use `.\scripts\Invoke-UiSmoke.ps1 -NoBuild -FullSuite` for the full suite.
- Prefer the runner over raw `dotnet test` for UI smoke. It polls progress and cleans up launched WPF/test processes.
- Avoid physical mouse-input assumptions in FlaUI tests. Prior failures came from `SendInput` access denied in this desktop session. Prefer UI Automation patterns: `Invoke`, `SelectionItem`, `Toggle`, and `Value`.
- Dialog discovery by top-level window title was unreliable. Prefer waiting for stable dialog controls or automation IDs.
- UI smoke tests should pass `--profile-store-path` when launching the app so profile persistence stays in a per-test writable path instead of user-local AppData.
- After interrupted UI runs, check for lingering `dotnet`, `testhost`, and `ServiceBusEmulatorExplorer.App` processes before rerunning.

## Service Bus And DLQ Behavior

- Service Bus entity management and message operations are separate concepts. Messages are not updated in place.
- Runtime and administration connection strings are both required; keep their validation explicit.
- DLQ replay must be non-destructive by default. Replaying creates a new active message and leaves the original DLQ message until a separate delete command is explicitly confirmed.
- Keep DLQ delete separate and visibly destructive. Multi-delete and visible-page delete need clear confirmation; visible-page delete uses typed confirmation.
- Keep message action labels intent-specific: use labels like `Send New Message`, `Replay DLQ Copy`, and `Edit and Replay DLQ`; avoid overlapping terms such as repair/resubmit/replay copy for the same action.
- When testing SDK model types, use Azure SDK model factories instead of inventing production seams only for tests.
- Be careful with sequence-number targeting in DLQ scans. Avoid loops that can keep receiving and abandoning the same non-target messages without progress.
- If replay send succeeds but abandoning the original DLQ lock fails afterward, do not report the replay send itself as failed.

## Product And Planning Lessons

- If a reference project is mentioned, ask whether it is authoritative or illustrative. The earlier Minimal API reference helped with endpoint and UI interaction shape, but it was not an architecture constraint for this WPF app.
- The intended workflow reference is closer to `paolosalvatori/ServiceBusExplorer`: menu bar, toolbar, namespace tree, tabbed entity view, message list/detail panes, action strip, and log pane. The visual treatment should be modern, not a legacy clone.
- Treat the MVP wireframe as a functional contract. If something appears in the wireframe, implement that workflow; if a reference tool has extra conveniences not in the MVP, do not leave them as placeholders.
- For this desktop operations tool, compact split-pane layouts clarify scope better than high-fidelity hero-style mockups.
- For dense action strips, prefer compact icon-plus-label buttons with clear tooltips and automation names. The visible label should explain the operation; automation names should remain stable for tests.

## WPF Architecture Lessons

- Keep `ShellViewModel` as orchestration, not the owner of every feature workflow. Prior quality gates forced extraction into `EntityManagementWorkflow` and `MessageInspectionViewModel`.
- Put domain and SDK mapping rules in focused core classes such as request factories, mappers, projections, and validators. Keep dialogs as input collectors, not Service Bus address builders.
- Profile-load failures should be surfaced through operation/log state rather than crashing before the main window appears.
- Keep operation-log timestamps UTC and explicit; timestamp display matters for message troubleshooting.
- Prefer typed metadata in core contracts. Do not push preformatted UI strings down into the core layer.
- For command timeouts around WPF dialogs, do not count user modal dialog time against SDK operation time. Start the bounded timeout when the admin/runtime call begins.
- Keep WPF layouts responsive. Avoid fixed-width rows, oversized minimum widths, or always-visible outer scrollbars that clip controls when the window is resized. The app should open large enough to show the primary workflow cleanly, while compact fields should shrink, use text trimming, or allow hidden in-field scrolling instead of forcing the whole shell to scroll horizontally.

## Plan And Checklist Work

- The staged plan may contain stale unchecked summary or Definition of Done checkboxes even after code exists. Verify repository evidence before adding new implementation.
- For stale markers, run the relevant tests/proofs, update only the justified checkbox or history note, and do not broaden the change.
- Do not mark a plan or DoD item complete from a script that only prints a checklist. If the item says rendered WPF/manual behavior, add or run a proof that actually exercises the WPF path.
- When using `$implement-it`, run the plan-compliance gate before the code-quality gate. If a quality fix changes behavior, return to plan-compliance before asking quality to re-check.
- If subagent gates hang, close them after bounded waits and restart with a narrower prompt. Do not claim a mandatory gate passed without an actual result.

## Packaging And Manual Proof

- Manual proof:

```powershell
.\scripts\Invoke-ManualProof.ps1
```

- Windows publish:

```powershell
.\scripts\Publish-Windows.ps1
```

- Publish output goes under `artifacts\publish\win-x64` and should remain ignored.
- NuGet vulnerability-feed warnings can appear when network/proxy access is restricted. Report them separately from build or test failures.
