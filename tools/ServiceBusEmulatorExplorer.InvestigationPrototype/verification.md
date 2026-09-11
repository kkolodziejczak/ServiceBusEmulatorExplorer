# Rendered prototype walkthrough

- PASS: Initial billing scope: 50 loaded of 120 fixture messages
- PASS: Initial multi-selection and focused inspector
PASS: Preview changes preserve checked messages; header and row checkbox centers align within 1 pixel.
PASS: Two ordinary checkbox clicks keep both messages checked without modifiers.
PASS: Row preview does not alter the checked set.
PASS: Header checkbox selects all loaded messages from a partial selection.
PASS: Select all batches selection notifications instead of notifying once per row.
PASS: Header checkbox clears all loaded messages.
PASS: Space on the header selects all, not a current row.
PASS: Space on a row checkbox toggles that checkbox once.
- PASS: Refresh preserves focused body and checked rows
- PASS: Load more adds a second page without replacing inspector
- PASS: Selecting a different grid row updates inspector
- PASS: Returning to a checked row updates preview without reducing selection
- PASS: Raw inspector preserves original body text
- PASS: Copy writes exact original body to clipboard
- PASS: Properties tab shows metadata
- PASS: Body always wraps to inspector width with no Wrap toggle
- PASS: Find selects a matching term in the rendered inspector
- PASS: Dead letter tab loads three DLQ fixtures
- PASS: DLQ properties show reason
- PASS: Replay adds an active copy while retaining original DLQ message and body
- PASS: Editing is disabled for multiple checked DLQ messages
- PASS: Batch replay creates one new send per checked message without deleting originals
- PASS: Cancelling Edit and Replay does not create a copy
- PASS: Replay editor opens with formatted JSON
- PASS: Rendered editor uses preview colors for keys, strings and scalars
- PASS: Editing a middle value preserves the caret and refreshes coloring
- PASS: Undo reverses text editing without a formatting-only step
- PASS: Redo restores exact edited JSON
- PASS: Incomplete JSON stays editable without rewriting text
- PASS: Editor always wraps and keeps horizontal scrolling disabled
- PASS: Replay editor rejects original message ID
- PASS: Long JSON strings wrap in the compact replay editor
- PASS: Edit and Replay leaves original DLQ body unchanged
- PASS: Returning to Active resets to 50 and shows edited copy with new ID
- PASS: Topic view shows copies from all three subscriptions
- PASS: Topic delivery identities stay unique across subscriptions
- PASS: Tree search filters unrelated branches
- PASS: Unknown total and malformed JSON remain inspectable
- PASS: Empty scope clears previous inspector
- PASS: Empty scope disables copy and paging
- PASS: Real 5-second automatic refresh adds sample arrivals and preserves focus
- PASS: Pause stops automatic arrivals
- PASS: Disconnect clears messages and inspector
- PASS: Reconnect restores a usable sample scope
PASS: Preview changes preserve checked messages; header and row checkbox centers align within 1 pixel.
PASS: Two ordinary checkbox clicks keep both messages checked without modifiers.
PASS: Row preview does not alter the checked set.
PASS: Header checkbox selects all loaded messages from a partial selection.
PASS: Select all batches selection notifications instead of notifying once per row.
PASS: Header checkbox clears all loaded messages.
PASS: Space on the header selects all, not a current row.
PASS: Space on a row checkbox toggles that checkbox once.
- PASS: 980 × 640 window: primary controls remain in bounds
- PASS: DLQ replay actions fit the compact window
- PASS: Search accepts keyboard focus
- PASS: Tab traversal advances from search
- PASS: Desktop layout restored after compact flow

PASS — rendered walkthrough completed. Screenshots require visual review; this does not claim a screen-reader audit or broker integration proof.
