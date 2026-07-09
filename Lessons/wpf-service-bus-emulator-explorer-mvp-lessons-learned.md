# WPF Service Bus Emulator Explorer MVP Lessons Learned

Date: 2026-07-08

- The initial term "CRUD operations" was ambiguous. Future Service Bus planning should immediately separate entity management from message operations because messages cannot be updated in place after enqueue.
- DLQ replay policy needed to be decided early. The durable default for explorer tools should be non-destructive replay, with delete as a separate explicit action.
- The example Minimal API was useful for endpoint behavior and UI interaction shape, but not as an architecture constraint. Future specs should ask whether a reference project is authoritative or illustrative before treating its backend as reusable.
- For this style of desktop operations tool, a compact split-pane wireframe clarified the Stage 1 scope better than a high-fidelity visual mock.
- When the user mentions a common existing desktop tool, identify the exact reference early and separate workflow from visual style. Here the intended workflow was paolosalvatori/ServiceBusExplorer: menu bar, toolbar, namespace tree, tabbed entity view, message list/detail panes, action strip, and log pane. The desired visual treatment is modern, not a legacy clone.
- For message-broker troubleshooting tools, ask timestamp-display policy early. UTC labels are important because enqueue, expiry, created, and log times are used to reason about ordering and failures.
- When the user says they want to implement what they see, treat the wireframe as a functional contract. Remove reference-tool conveniences that are not in the MVP instead of leaving them as visual placeholders.
- Message actions need intent-based labels. Use "Send New Message" for blank authoring, "Replay DLQ Copy" for cloning without edits, and "Edit and Replay DLQ" for edited replay drafts; avoid overlapping legacy labels such as repair/resubmit/replay copy in the same surface.
- Integration tests should use the same Docker Compose emulator that developers run locally, but remain opt-in behind an environment variable so normal unit tests do not require Docker.
- Service Bus integration tests do not prove WPF rendering or desktop interaction. Treat WPF UI automation as a separate small smoke-test layer with its own explicit run command.
- For WPF tools, "after the shell stabilizes" is too late if the UI is the product. Add a tiny gated FlaUI launch smoke test as soon as the first shell exists, then grow UI coverage only around regression-prone workflows.
- For dense desktop action strips, prefer compact icon-plus-label buttons with full command names in tooltips and automation names. This keeps the UI scannable while preserving testability and accessibility.
- For context-sensitive WPF command surfaces, distinguish "does not apply here" from "applies here but is currently blocked." Hide commands that do not belong to the selected entity context, but keep valid blocked commands visible, disabled, and explained by tooltip text.
- Topic-level convenience workflows should not silently change the current inspection model. Refreshing a topic's child subscriptions can update counts and per-subscription caches, while message grids should remain scoped to a selected queue or subscription unless a combined topic view is explicitly planned.

Related spec: ../specs/wpf-service-bus-emulator-explorer-mvp.html
