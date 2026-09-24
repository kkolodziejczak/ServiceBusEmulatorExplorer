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

## Public Documentation Boundaries

- Keep the root `README.md` user-facing: what the application does, how to download it, how to connect and use it safely, where to get help, and the license.
- Put human contribution setup and pull-request expectations in `CONTRIBUTING.md` and detailed test commands in `tests/README.md`.
- Keep maintainer-only release mechanics in this `AGENTS.md`; do not expose tagging and publishing procedures as contributor instructions.
- Keep repository architecture, implementation constraints, agent workflows, and internal development lessons in this `AGENTS.md` instead of expanding the README with technical internals.
- When behavior changes, update the narrowest authoritative document and avoid duplicating the same technical procedure across public-facing files.

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

## Launching for user testing

- When the user asks to open the app for manual testing, launch it outside the sandbox using `exec_command` with `sandbox_permissions: "require_escalated"`, so it opens on the user's normal desktop. Use a visible window and preserve the requested mode; for dummy-data Message Workbench testing, pass `--message-workbench-prototype` and an isolated `--profile-store-path`.
- A running process or window handle alone does not prove the user can see the app: sandbox launches previously returned both while remaining invisible to the user. If the outside-sandbox launch is blocked, report that limitation instead of claiming the app is ready for manual testing.

## App-only WPF screenshot proof

- For visual verification without desktop capture/control, first use the existing [WPF screenshot harness](tools/ServiceBusEmulatorExplorer.ReadmeScreenshot/Program.cs). It instantiates the real app window with synthetic services, invokes WPF routed actions, waits for layout, and captures only the window content through [RenderTargetBitmap](tools/ServiceBusEmulatorExplorer.ReadmeScreenshot/WpfScreenshot.cs). Missing desktop-control tools alone do not block this route. It still requires a working Windows/WPF runtime.
- Inspect the harness before running it. Build its project when source freshness matters; use a 60-second process deadline for build and each capture. Save proof images to the current task's writable artifact directory, never over the README screenshot. A fresh capture from an existing binary proves that binary, not unbuilt source changes.

```powershell
dotnet build tools/ServiceBusEmulatorExplorer.ReadmeScreenshot/ServiceBusEmulatorExplorer.ReadmeScreenshot.csproj --no-restore
# Set $proofOutput to a new absolute PNG path in the current task's artifact directory.
dotnet tools/ServiceBusEmulatorExplorer.ReadmeScreenshot/bin/Debug/net10.0-windows/ServiceBusEmulatorExplorer.ReadmeScreenshot.dll $proofOutput --message-library-prepare
```

- The wide workbench capture is 1642x958. Other current modes cover 1500/1100/980 widths, compact Author, Properties, Variables, Single, and mapping/capture/review/results dialogs; inspect `Program.cs` for exact switches. On 2026-09-22 the existing binary successfully produced a fresh wide capture without desktop capture/control.
- Open and inspect every proof image against the approved visual contract. For interaction proof, assert state before and after real routed actions and capture the resulting state; a directly constructed dialog or restored result fixture proves appearance only, not reachability or a completed workflow. Extend this harness within an approved task when a required state is missing.
- Report rendering, routed interactions, UI Automation, and physical-input/accessibility proof separately. App-only screenshots do not establish broker correctness, physical keyboard/mouse behavior, screen-reader support, or multi-monitor/DPI correctness. Mark only the unavailable proof blocked, with the actual failure evidence.

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

- For every UI proposal or UI change, read [the UI language and component registry](specs/ui-language.md), the applicable approved feature visual contract, and the production [shared WPF styles](src/ServiceBusEmulatorExplorer.App/Investigation/Resources/SharedStyles.xaml) before drawing or coding. Reuse approved components and their documented states. Record each genuinely new UI concept or component in the registry with its purpose, states, usage locations, and approval status; validate those paths. A proposed component does not become approved merely because it appears in a generated mockup or prototype.
- For a new UI concept, capture the current running app first and edit that capture to propose only the changed area. Obtain the user's explicit approval of the final mockup before implementing the UI; approval of a direction or variant alone is not build approval. Carry forward unchanged chrome and components from the actual app rather than recreating them in a separate mock window.
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

## Maintainer Release Process

- Only an authorized maintainer creates release tags or publishes GitHub Releases. Contributors should not be instructed to prepare or push tags.
- Releases use strict `vMAJOR.MINOR.PATCH` tags and provide two Windows x64 executables:
  - `ServiceBusEmulatorExplorer-vX.Y.Z-win-x64-portable.exe`, which includes the .NET runtime;
  - `ServiceBusEmulatorExplorer-vX.Y.Z-win-x64-requires-dotnet10.exe`, which requires the .NET 10 Desktop Runtime.
- Use the guarded helper instead of pushing a branch and annotated tag together:

```powershell
.\scripts\Push-ReleaseTag.ps1 -Tag vX.Y.Z
```

- Run the helper with `-WhatIf` first. It requires a clean branch tracking `origin/<branch>`, a GitHub.com origin, authenticated GitHub CLI access, and a tag that does not already exist locally or remotely. It never force-pushes.
- The helper pushes the checked-out branch with `push.followTags=false`, verifies the active release workflow on GitHub, and then creates and pushes only the requested tag.
- The tag workflow validates the repository, publishes and checks both artifacts, launch-smokes both final executables, and creates or updates a draft release. It must never publish automatically.
- Before publishing the draft, inspect the tag, generated notes, asset names, download guidance, validation logs, and launch-smoke results. Keep the unsigned-executable and SmartScreen warning in the release notes.
- If a GitHub-hosted desktop session blocks FlaUI, preserve the successful build and artifact-validation logs, run both explicit `SBE_APP_EXE` launch-smoke commands in an interactive Windows session, and record a proof reference with both SHA-256 hashes.
- The hosted-smoke fallback uses the existing strict tag and workflow-dispatch inputs `use_manual_launch_smoke_proof` and `manual_launch_smoke_proof_reference`. The `manual-launch-smoke` GitHub Environment must have required reviewers. This fallback skips only hosted launch proof and still creates only a draft.
- Publishing a release triggers `.github/workflows/update-readme-screenshot.yml`. That workflow builds the deterministic screenshot generator from the released tag, validates the PNG, pushes a dedicated automation branch only when the rendered UI changed, dispatches fast CI for that branch, and writes an `Open pull request` link to the workflow summary. A maintainer must open and merge that pull request; repository-wide permission for GitHub Actions to create or approve pull requests must remain disabled. Do not replace the README screenshot manually.
