# Correlation investigation walkthrough

Actual rendered WPF with synthetic messages; routed actions, no physical pointer automation.

- PASS: Search button visible label is vertically centered (offset 1,0px)
- PASS: Opening scope loads its first 50 sample messages
PASS: Preview changes preserve checked messages; header and row checkbox centers align within 1 pixel.
PASS: Two ordinary checkbox clicks keep both messages checked without modifiers.
PASS: Row preview does not alter the checked set.
PASS: Header checkbox selects all loaded messages from a partial selection.
PASS: Select all batches selection notifications instead of notifying once per row.
PASS: Header checkbox clears all loaded messages.
PASS: Space on the header selects all, not a current row.
PASS: Space on a row checkbox toggles that checkbox once.
- PASS: Refresh preserves preview and checked messages
- PASS: Load more retains preview and adds another page
- PASS: Loaded rows expose typed correlation IDs
- PASS: Message grid includes a correlation ID column
- PASS: Raw tab retains the unformatted original body
- PASS: Properties tab retains correlation metadata
- PASS: Find selects a match in the inline JSON editor
- PASS: Primary controls fit the 1500 × 900 viewport
- PASS: desktop: namespace typing renders entity suggestions
- PASS: desktop: namespace suggestions match complete entity paths
- PASS: desktop: namespace typing filters the tree live
- PASS: desktop: namespace clear action is visible and accessible
- PASS: desktop: repeated Down advances namespace suggestions
- PASS: desktop: Up returns to the first namespace suggestion
- PASS: desktop: Escape dismisses suggestions without clearing the namespace filter
- PASS: desktop: Enter fills the full subscription path and opens its messages
- PASS: desktop: clicking a queue suggestion opens that queue
- PASS: desktop: unmatched namespace search shows its empty state
- PASS: desktop: clear restores the entire namespace tree
- PASS: desktop: clearing namespace search preserves the open entity
- PASS: desktop: namespace search preserves MessageCount and DLQ totals
- PASS: compact: namespace typing renders entity suggestions
- PASS: compact: namespace suggestions match complete entity paths
- PASS: compact: namespace typing filters the tree live
- PASS: compact: namespace clear action is visible and accessible
- PASS: compact: repeated Down advances namespace suggestions
- PASS: compact: Up returns to the first namespace suggestion
- PASS: compact: Escape dismisses suggestions without clearing the namespace filter
- PASS: compact: Enter fills the full subscription path and opens its messages
- PASS: compact: clicking a queue suggestion opens that queue
- PASS: compact: unmatched namespace search shows its empty state
- PASS: compact: clear restores the entire namespace tree
- PASS: compact: clearing namespace search preserves the open entity
- PASS: compact: namespace search preserves MessageCount and DLQ totals
- PASS: Typing a correlation prefix suggests known IDs
- PASS: Suggestion popup is rendered while typing
- PASS: Repeated Down advances to the second suggestion
- PASS: Up returns to the first suggestion
- PASS: Keyboard Down and Enter choose a complete correlation ID
- PASS: Applied search changes the primary action to Clear search criteria
- PASS: Changing the ID offers a new search
- PASS: Emptying the input still allows clearing the applied filter
- PASS: Primary Clear search criteria removes filter and restores browsing
- PASS: Global correlation search completes visibly
- PASS: Search button visible label is vertically centered (offset 0,5px)
- PASS: Global results match the complete correlation ID exactly
- PASS: Search returns every matching fixture across the namespace, including later pages
- PASS: Correlation results include multiple entity locations
- PASS: Correlation results include both active and dead-letter messages
- PASS: Global search scans beyond the first page
- PASS: Rendered search status reports scan progress or completion
- PASS: Correlation copy icon sits within 10 pixels of its ID
- PASS: Copy correlation sends the exact ID to the clipboard for log lookup
- PASS: Find related searches the focused message correlation globally
- PASS: Correlation matching is case sensitive
- PASS: Clear search returns to the entity workspace
- PASS: Inline JSON opens formatted without changing original bytes
- PASS: Formatting alone leaves default Replay action and clean state
- PASS: Inline JSON wraps to available inspector width
- PASS: Rendered editor uses preview colors for keys, strings and scalars
- PASS: Editing a middle value preserves the caret and refreshes coloring
- PASS: Undo reverses text editing without a formatting-only step
- PASS: Redo restores exact edited JSON
- PASS: Incomplete JSON stays editable without rewriting text
- PASS: Editor always wraps and keeps horizontal scrolling disabled
- PASS: Default replay ID includes a readable replay suffix
- PASS: Inspector displays the proposed replay ID
- PASS: Untouched replay retains the DLQ original
- PASS: Repeated replay advances the numbered default ID
- PASS: A second replay advances the counter again
- PASS: Fixture includes a meaningful editable JSON value
- PASS: Changing JSON exposes Modified and Edit and Replay
- PASS: A dirty draft exposes Discard changes
- PASS: Switching rows preserves the original row's draft
- PASS: Discard restores original formatted JSON and Replay label
- PASS: Incomplete JSON cannot be sent as an edited replay
- PASS: Edited replay leaves original DLQ body unchanged
- PASS: Untouched replay copies exact original body bytes and preserves correlation
- PASS: Edited JSON is sent in the new active copy with the same correlation
- PASS: Global results provide two DLQ messages for batch selection
- PASS: Mixed active and DLQ checks block replay instead of silently skipping active rows
- PASS: Batch selection with one draft blocks replay
- PASS: Focusing a clean row does not bypass a checked draft
- PASS: Multiple checked drafts require individual replay or discard
- PASS: Discarding one draft does not ignore another checked draft
- PASS: Two checked DLQ results form a replay batch
- PASS: Batch replay retains both DLQ originals
- PASS: Completed zero-result search clears stale rows and focused message
- PASS: No-results state is rendered in list and inspector
- PASS: No-results state offers no stale replay action
- PASS: Canceled zero-result scan explicitly says search incomplete
- PASS: Empty search clear action restores entity browsing
PASS: Preview changes preserve checked messages; header and row checkbox centers align within 1 pixel.
PASS: Two ordinary checkbox clicks keep both messages checked without modifiers.
PASS: Row preview does not alter the checked set.
PASS: Header checkbox selects all loaded messages from a partial selection.
PASS: Select all batches selection notifications instead of notifying once per row.
PASS: Header checkbox clears all loaded messages.
PASS: Space on the header selects all, not a current row.
PASS: Space on a row checkbox toggles that checkbox once.
- PASS: Primary controls fit the 1100 × 800 viewport
- PASS: Compact inspector retains JSON wrapping
- PASS: Primary controls fit the 980 × 640 viewport
- PASS: Minimum viewport retains four lines of usable JSON editor
- PASS: Minimum viewport displays at least one complete message row
- PASS: Message ID text fits fully inside the compact row
- PASS: Minimum viewport exposes replay and discard for a draft
- PASS: Plain text and malformed JSON are labelled and preserved exactly
- PASS: Plain text and malformed JSON are labelled and preserved exactly
- PASS: Valid JSON returns to the formatted JSON label
- PASS: Exiting search through the tree resumes five-second automatic refresh and preserves preview
- PASS: Pausing automatic refresh prevents incoming sample messages
- PASS: Disconnect clears rows and focused inspector
- PASS: Disconnected state hides stale replay controls
- PASS: Reconnect restores a focused first page of sample messages
