# Responsive Workbench Compose

Approved 2026-10-02: [hybrid mockup](design/approved/18-responsive-workbench.png), refined by the user to require one property column at small widths.

- Wide editing area: editable JSON body beside two property groups (message properties and application properties/destination).
- Medium editing area: Body / Properties / Variables tabs; Properties has two groups side by side.
- Small editing area: the same tabs; Properties has one stacked column.
- Measure usable editor space after navigation panes; keep fields compact and left aligned, wrap labels/actions as needed, and use vertical scrolling instead of clipping or whole-form horizontal scrolling.
- Resize the existing controls without reloading the template. Preserve JSON, properties, variables, destination, tree selection, and editing context. Keep Variables, reply/routing, inline property creation/deletion, copy, rename, save, and warning remedies available.
- New windows start at 1200x800; retain saved user dimensions and the 980x640 minimum.
- Generated omissions in the mockup are not feature removal. Namespace-filter and automatic namespace-width proposals remain separate pending work.

Verification covers all three modes in the real WPF window, resizing the same draft in both directions, property editing, Variables, destination remedy navigation, and Prepare/back transitions. RenderTargetBitmap and routed actions are distinct from physical keyboard, screen-reader, and DPI proof.

## Verification

92 focused Workbench and preference tests passed. The real-window `--responsive-compose` harness captured 13 states at 1700, 1695, 1500, 1200 and 980 DIPs, with same-window resize, Variables/body focus, draft/tree/property/default retention, Prepare/back and compact scroll-end assertions. Independent code and image review passed after correcting the wide Variables heading. Build completed with zero warnings. Local evidence is in `artifacts/responsive-workbench-proof`.

Rendered geometry and routed actions passed. Physical keyboard/mouse input, screen-reader output, and multi-monitor/DPI behavior were not verified. Existing prototype send simulation and session-only library behavior are unchanged.
