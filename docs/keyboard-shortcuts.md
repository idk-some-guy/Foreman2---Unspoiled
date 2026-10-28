# Keyboard shortcuts

Foreman 2 on the Mac uses ⌘ for its command shortcuts. On Linux, read Ctrl wherever this page says ⌘.

## Menu commands

| Menu | Command | Shortcut |
|---|---|---|
| Foreman 2 | Settings… | ⌘, |
| Foreman 2 | Hide Foreman 2 | ⌘H |
| Foreman 2 | Quit Foreman 2 | ⌘Q |
| File | New Graph | ⌘N |
| File | Open… | ⌘O |
| File | Save | ⌘S |
| File | Save As… | ⇧⌘S |
| File | Export Image… | ⇧⌘E |
| File | Close | ⌘W |
| Edit | Select All | ⌘A |
| Edit | Deselect All | ⇧⌘A |
| Edit ▸ Find | Find Node… | ⌘F |
| Edit ▸ Find | Find Next | ⌘G |
| Edit ▸ Find | Find Previous | ⇧⌘G |
| Edit ▸ Find | Select Next Node | ⇥ (only while the canvas has focus) |
| Edit ▸ Find | Select Previous Node | ⇧⇥ (only while the canvas has focus) |
| View | Zoom In | ⌘+ |
| View | Zoom Out | ⌘− |
| View | Actual Size | ⌘0 |
| View | Fit to Graph | ⇧⌘0 |
| View | Show Gridlines | ⌥⌘G |
| View | Pause All Calculations | ⌥⌘P |
| View | Show Inspector | ⌥⌘I |
| View | Pin Inspector | ⇧⌥⌘I |
| View | Show Toolbar | ⌥⌘T |
| View | Enter Full Screen | ⌃⌘F (Mac only) |
| Window | Minimize | ⌘M |
| Window | Welcome to Foreman | ⇧⌘1 |
| Help | Foreman 2 Help | ⌘? |

⌘. cancels whatever is in progress: a running solve first, then an open popover or canvas panel, then the frontmost sheet. No menu item carries it.

Cut, Copy, Paste and Delete appear in the Edit menu without shortcuts, because the canvas handles those keys itself (see below).

## Canvas keys

These work while the canvas has keyboard focus.

| Key | Action |
|---|---|
| ⇥ / ⇧⇥ | Select the next or previous node in reading order (top to bottom, then left to right) and wrap at the ends |
| ⌃⇥ / ⌃⇧⇥ | Leave the canvas for the next or previous stop in the window |
| ⌥ + ← → ↑ ↓ | Select the nearest node in that direction |
| ⇧⌥ + ← → ↑ ↓ | Add the nearest node in that direction to the selection |
| ⌥⇥ / ⌥⇧⇥ | From a node, step through its links (outputs left to right, then inputs); from a link, step to the node's next or previous link |
| Return | Open the inspector for the focused node and move focus into it |
| ← → ↑ ↓ | Move the selected nodes and annotations by one grid unit |
| ⇧ + ← → ↑ ↓ | Move them by a larger step (the major grid unit when one is set) |
| W A S D | Pan the view |
| ⇧ + W A S D | Pan five times as far |
| ⇧ (held while dragging) | Lock the drag to one axis |
| ⌘C / ⌘X / ⌘V | Copy, cut or paste the selection |
| Delete | Delete the selected annotations, or the selected nodes if no annotation is selected (asks first when more than ten are selected) |
| Space | Show or hide the gridlines |
| Escape | Cancel a link drag or a shape being drawn, or close an open panel; otherwise clear the selection and hide the focus ring. From a focused link, return to its node. In the inspector, return to the canvas and the same node |
| ⌃⇧⇥ | In the inspector, return to the canvas and the same node |
