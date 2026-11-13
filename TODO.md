# TODO

Open work as of 2026-10-27. Each item names the branch or T3 session to continue from.

## Branches

- `spike/avalonia-delete-dialog` (draft PR #51): the Delete messages dialog on Avalonia,
  rendered and compared on Windows, Linux and macOS. CI passes on all three. Decide whether
  the Avalonia migration continues from here. Session: "Assess Avalonia Cross-Platform Migration".
- `t3code/explore-agent-cli-mcp`: read-only agent access through a local MCP server, with a test
  that no tool returns connection strings or tokens. 183 commits behind master; conflicts in
  `CAPABILITIES.md`, `DECISIONS.md`, `specs/ui-language.md` and two `InvestigationWindow` files.
  Rebase onto master before review. Known gap: on native Windows, agent deny rules don't stop a
  shell from reading `connection-profiles.json`. Session: "Expose Service Bus Tools to Agents".
- `other/workbench-library-search`: WIP commit for Workbench library search. Conflicts with master
  in `MessageLibraryPrototypeView.Search.cs`, `WorkbenchLibrarySearch.cs` and
  `WorkbenchLibrarySearchTests.cs`.

## Not committed

- Main checkout: proposed design images under `specs/message-library/design/proposed/` and
  `specs/app-icon/`, and `.bugstash.json`. Keep (commit) or delete.

## Follow-ups

- Release signing (issue #7, DEC-059): the manual launch-smoke fallback (`use_manual_launch_smoke_proof`)
  rebuilds and re-signs, so the hashes in its proof reference describe other files than the drafted
  ones, and it costs a second approval and phone code. The `manual-launch-smoke` environment it names
  does not exist yet, although AGENTS.md says it must have required reviewers. Let the fallback reuse
  the failed run's `signed-release` artifact, and create the environment with a reviewer.
