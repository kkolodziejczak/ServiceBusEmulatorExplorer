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
