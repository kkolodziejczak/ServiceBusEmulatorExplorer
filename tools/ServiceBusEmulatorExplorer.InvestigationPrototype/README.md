# Investigation workspace prototype

This throwaway WPF prototype explores the layout approved on September 11, 2026. It lives on `prototype/investigation-workspace` and uses only synthetic, in-memory data. It has no Service Bus client, network access, credentials, or profile persistence.

Run from the repository root:

```powershell
dotnet run --project tools/ServiceBusEmulatorExplorer.InvestigationPrototype
```

The prototype is deliberately separate from the production application and release solution. Close its window to stop it. Restarting resets its sample state.

## Design authority

[Approved layout](approved-layout.png): searchable namespace tree with aligned Messages/DLQ totals, message list with multi-selection, and a spacious JSON inspector. The explanatory notes underneath the tree were explicitly removed. Connection settings, entity administration, sending, replaying, and deletion are outside this prototype.

The user approved this visual direction; whether the interactions feel right remains the question for this prototype. The next implementation should use the existing SDK services behind a properly tested state model, rather than promote throwaway prototype code directly into production.

![Rendered WPF prototype](preview.png)

## Try it

1. Start with `order-events / billing`. Check multiple messages while opening a different event in the inspector.
2. Switch between Active and Dead letter. Inspect JSON, original Raw text, and Properties. Try Copy, Find, and Wrap.
3. Refresh and load another page. The focused message and checked rows should remain stable.
4. Choose an automatic refresh interval and pause/resume it. Sample incoming messages demonstrate updates without contacting a broker.
5. Select the `order-events` topic to inspect subscription copies with their source identity.
6. Search the namespace tree. Try `audit-events` for unknown counts and non-JSON bodies, and `empty-queue` for an empty result.
7. Disconnect and reconnect the sample profile. Resize the window: the inspector moves below the list on narrow windows.

Numbers are fixture values. A loaded page is distinct from an entity's total; topic totals sum subscription deliveries. These distinctions remain part of the future API contract even though the tree no longer carries explanatory notes.

## Verification

The executable can exercise the rendered WPF controls and capture screenshots:

```powershell
dotnet run --project tools/ServiceBusEmulatorExplorer.InvestigationPrototype -- --verify artifacts/investigation-prototype-proof
```

This bounded proof mode closes after verification and writes its report and images to the supplied directory. It is a prototype acceptance aid, not a production regression suite or an emulator integration test.

## Verified on September 11, 2026

Build passed with zero warnings and errors. The [rendered walkthrough](verification.md) passed 27 interaction and state checks. Screenshots were visually inspected after repairs.

| UI gate | Result | Evidence |
| --- | --- | --- |
| Design | PASS | Compared with approved image; totals retained and sidebar notes absent. |
| Rendered states | PASS | Active/DLQ, JSON/raw/properties, exact clipboard copy, find, wrap, refresh, paging, real timer/pause, empty, malformed body, disconnect/reconnect. |
| Geometry | PASS | Desktop 1500×900 and compact 980×640 windows; aligned tree totals, readable topic source names, stacked inspector at compact size. |
| Basic accessibility | PASS | Named controls, keyboard focus and Tab traversal checked; numeric DLQ values accompany color emphasis. |
| Final sweep | PASS | Repeated complete walkthrough and visually inspected refreshed captures after repairs. |

Limits: no broker integration, full screen-reader audit, or exhaustive DPI matrix. Sample arrivals are deliberately simulated. Window and pane sizes reset when the prototype restarts. The proof exercises WPF events and automation peers; it does not emulate every physical pointer gesture.
