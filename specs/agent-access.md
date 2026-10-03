# Agent access (MCP)

Status: first slice implemented 2026-10-02 (marked "done" below); the rest is planned. Decisions:
DEC-023, DEC-024 and DEC-025 in [DECISIONS.md](../DECISIONS.md). Both Settings mockups were approved
on 2026-10-02. Code: `src/ServiceBusEmulatorExplorer.App/Agent/` and
`src/ServiceBusEmulatorExplorer.App/Investigation/InvestigationWorkspace.Agent.cs`.

## Goal

A user points an AI agent (Claude Code, Codex CLI, GitHub Copilot) at the running desktop app and
asks it things like "I have this message open, find out why it is failing". The agent reads what
the app sees. The desktop app stays the primary tool for people.

## Scope

**Version 1 (read-only)**

| Tool | Returns | State |
| --- | --- | --- |
| `get_app_state` | profile name and kind (never connection strings), connection state, selected entity and bucket, focused message identity, watch summary, server time UTC | done (watch rules included) |
| `get_focused_message` | the inspector's message: body, properties, `DeadLetterReason`, `DeadLetterErrorDescription`, `DeadLetterSource`, delivery count, UTC times, source entity | done |
| `list_entities` | queues, topics and subscriptions with settings (max delivery count, lock duration, TTL, sessions, duplicate detection) and counts, keeping the unavailable/stale count states | done |
| `list_fetched_messages` | messages the app peeked this session (browse, search, watch, agent peeks), newest first, paged; reports eviction | planned |
| `peek_messages` | non-consuming peek of an entity's Active or DLQ, bounded | done |
| `search_messages` | message/correlation ID search with the app's grammar, bounded by delivery and time budgets | planned |
| `list_watches` | watch rules and resolved targets for the current profile | planned (rules are in `get_app_state`) |
| `get_watch_arrivals` | arrivals after a cursor; returns the next cursor, or an expired-cursor result after eviction | done |

**Later**

- Replay a DLQ copy through the app's own confirmation dialog (DEC-001). Queue replay has no dialog
  today, so an explicit confirmation entry point is needed first.
- Message Workbench: read templates, add drafts the user reviews and sends. Waits until Workbench
  sends to a real broker and has a stored library.
- Push of watch arrivals into Claude Code through a channel relay (`--channel`), see below.

**Never**: delete of messages or entities (DEC-002), returning connection strings or the access
token, consuming or settling messages.

## Behaviour

- **Switch**: Settings has "Allow agent access", off by default. When on, the server starts with the
  app and keeps running while the app is in the tray; turning it off stops the listener and closes
  open requests.
- **Endpoint**: `http://127.0.0.1:<port>/mcp`, default port 47811, changeable in Settings. A port
  that cannot be bound (another instance, another program) is shown as an actionable validation
  that opens Settings and focuses the port (DEC-013). No dynamic port.
- **Token**: generated on first enable, stored with the existing user-bound DPAPI protection in the
  preferences file, shown masked with Copy and Regenerate. Every request must carry
  `Authorization: Bearer <token>`.
- **Agent configuration**: Settings offers copyable snippets:
  - Claude Code: `claude mcp add --transport http sbe http://127.0.0.1:<port>/mcp --header "Authorization: Bearer <token>"`
  - Codex CLI (`~/.codex/config.toml`): `[mcp_servers.sbe]`, `url = "http://127.0.0.1:<port>/mcp"`, `bearer_token_env_var = "SBE_MCP_TOKEN"`
  - GitHub Copilot (CLI and desktop app read `~/.copilot/mcp-config.json`; VS Code uses `mcp.json`):
    `"sbe": { "type": "http", "url": "http://127.0.0.1:<port>/mcp", "headers": { "Authorization": "Bearer <token>" }, "tools": ["*"] }`
- **Visibility**: the main window shows when agent access is on and when an agent has called it
  recently. Message bodies go to the agent's model provider; the Settings description says so.
- **Timestamps** are explicit UTC (DEC-004).

## Per-profile access

DEC-025: each connection profile has "Allow agent access" in Settings › Connections, below the
connection warning. It is on for new profiles and for profiles saved before the switch existed.

- The global switch (Settings › Agents) decides whether the server runs; the active profile's
  switch decides whether the server may expose anything. Agent configuration stays valid when you
  move between profiles.
- While the active profile blocks agents, every tool returns the error "Agent access is turned off
  for the current connection profile." `get_app_state` reports only that access is blocked: no
  profile name, entity, message, count or watch data.
- The session recorder and the arrival journal record nothing while a blocked profile is active;
  both are already cleared on every profile switch, so data from an allowed profile is never shown
  after switching to a blocked one.
- The allow check is part of the Dispatcher snapshot, so state and permission are read together.
  Broker calls made for an agent (`peek_messages`, `search_messages`) check again when they finish
  and discard the result if the profile changed or now blocks access; a switch to a blocked profile
  cancels running agent operations.
- Settings › Agents shows the paused state ("Paused: the current connection does not allow agent
  access"), and so does the main-window indicator.
- Stored as an optional per-profile field in the preferences file (version 1); missing means
  allowed.

## Architecture

- `AgentAccessHost` (App): owns the `HttpListener` lifecycle behind an interface so Kestrel can
  replace it (DEC-024). Per request: loopback check, exact `Origin` parsing (absent is allowed,
  present must be `http://127.0.0.1:<port>` or `http://localhost:<port>`, otherwise 403), bearer
  check (401), size limit, then a stateless `StreamableHttpServerTransport` and `McpServer` from
  `ModelContextProtocol.Core`. `GET` and `DELETE` return 405.
- **State reads**: tools call a snapshot service that runs a short projection on the WPF
  Dispatcher and returns immutable DTOs (profile DTO without secrets, copied property
  dictionaries, connection generation, capture time). Serialisation happens off the UI thread.
- **Session recorder**: a bounded recorder at the session's `IServiceBusMessageService.PeekMessagesAsync`
  keeps fetched deliveries across entity switches; it is cleared on profile switch or disconnect.
- **Arrival journal**: watch arrivals are appended where `MessageWatchWorkflow` accepts them, with a
  sequence number, app-run id and connection generation; the journal is independent of
  acknowledgement in the UI.
- **Agent search** uses its own `DeliverySearch` instance; starting a scan on the UI's instance
  would cancel the user's search.
- **Preferences**: new optional fields at store version 1; a missing switch loads as off. Changing
  the version constant would reject existing files.
- **Packages**: `ModelContextProtocol.Core` pinned (DEC-019), about 1.9 MiB added; no ASP.NET Core.

## Channel push (later)

Claude Code channels are a research preview: Claude Code spawns a stdio server that declares
`experimental.claude/channel` and emits `notifications/claude/channel` (`content`, `meta`). Custom
channels load only with `--dangerously-load-development-channels server:<name>`, Team and
Enterprise organisations must enable channels, and a channel negotiating protocol 2026-07-28 is not
registered, so the relay pins 2025-11-25. The relay would be the app's exe started with
`--channel`, branching before any window or preferences load, and forwarding arrivals from the
running app. Codex and Copilot have no push; they use `get_watch_arrivals`.

## Investigation evidence

Done 2026-10-02 with throwaway spikes (not committed) and a paired Codex review.

- `ModelContextProtocol.Core` 2.2.0 exposes `StreamableHttpServerTransport`,
  `StreamServerTransport` and `McpServer.Create(ITransport, ...)`; `ModelContextProtocol.AspNetCore`
  requires the `Microsoft.AspNetCore.App` framework.
- A spike served MCP with `HttpListener` on `127.0.0.1` without admin rights, stateless, with a
  bearer token. Missing token → 401, foreign `Origin` → 403. Real clients listed and called a tool:
  Claude Code 2.1.287 and Copilot CLI 1.0.87 (the engine of the Copilot desktop app) over protocol
  2026-07-28, Codex CLI 0.160.0 over the older `initialize` handshake; the SDK client also passed on
  2025-11-25 and 2025-06-18. Codex CLI logged a non-fatal `relative URL without a base` on a side
  request; check it during implementation.
- A WinExe (GUI subsystem) launched with redirected pipes read stdin and wrote stdout and stderr.
- Microsoft does not recommend `HttpListener` for new development (limited servicing); accepted
  for a Windows-only loopback listener, kept replaceable.
- The runtime-required single-file exe was 6.60 MiB against the 15 MiB release limit.
- Gaps found in the code: `DeadLetterSource` is not projected; browse, search and watch do not
  retain everything fetched; the app has no single-instance guard and no `--channel` branch; the
  Workbench sends simulated acknowledgements only.

## Open items

- Approved designs: [Agents tab](agent-access/settings-agents-tab-proposal.png) and
  [per-profile switch](agent-access/settings-profile-agent-access-proposal.png), described in
  [ui-language.md](ui-language.md#settings--agents-tab-approved-2026-10-02); registered in DESIGN.md.
- The main-window indicator (on, recently used, paused by the profile) is not designed yet.
- `list_fetched_messages` needs the session recorder; `search_messages` needs its own
  `DeliverySearch`; retention limits for the recorder (count and memory) are open.
