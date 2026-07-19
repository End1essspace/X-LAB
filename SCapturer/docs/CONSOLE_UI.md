# SCapturer Console UI Contract

## Purpose

P6 turns the temporary milestone menu into the primary management interface for SCapturer.

The console UI must remain responsive while pixel acquisition, PNG encoding, disk persistence, clipboard publication, diagnostics, and benchmark work execute outside the console thread.

## Pages

### Dashboard

Provides:

- current listener and capture-pipeline state;
- benchmark state;
- display topology;
- active hotkeys;
- last-capture summary;
- access to every management page;
- full and region capture commands;
- process exit.

### Capture Settings

Provides:

- clipboard-copy toggle;
- capture-sound toggle;
- current lossless image policy.

### Hotkeys

Provides:

- full-capture binding;
- region-capture binding;
- exit binding;
- default restoration.

Hotkeys are entered as text. At least one modifier and one primary key are required.

Examples:

```text
Ctrl+Shift+G
Ctrl+Alt+PrintScreen
Win+Shift+S
Alt+F10
```

Update sequence:

1. parse and normalize the requested combination;
2. reject duplicate SCapturer bindings;
3. unregister current bindings on the hotkey STA thread;
4. ask Windows to register the complete candidate set;
5. persist settings only after successful registration;
6. restore the previous set if registration fails.

The application never leaves a partially applied candidate set active.

### Save Locations

Provides:

- full-capture folder editing;
- region-capture folder editing;
- opening both folders.

### Diagnostics

Provides:

- diagnostics logging toggle;
- baseline benchmark launch;
- diagnostics folder opening.

### Recent Captures

Displays up to twelve recent PNG files from the configured full and region folders.

Controls:

- `Enter` — open selected file;
- `F` — open selected file's containing folder;
- `R` — rescan both capture folders.

Recent history is filesystem-derived and does not require a database.

### About

Displays the current runtime, capture, geometry, and interface boundaries.

## Navigation

Global console controls:

- `↑` / `↓` — move selection;
- `J` / `K` — alternative selection controls;
- `Home` / `End` — jump to first or last item;
- `Enter` — activate selected item;
- `1`–`9` — activate a visible menu item directly;
- `Esc` / `Backspace` — return to Dashboard.

Selection is retained independently for each page.

## Differential rendering

The renderer constructs a complete text frame but does not write the complete frame on every update.

For each render:

1. read the current console width and height;
2. normalize and truncate visible lines;
3. compare each line with the previously rendered line;
4. reposition the cursor only for changed lines;
5. clear only lines that disappeared;
6. preserve the selection and page state.

A full clear is allowed only when:

- entering a text prompt;
- changing pages;
- the terminal is resized;
- the renderer recovers from unsupported cursor operations.

Capture-pipeline state changes therefore update the status area without repeatedly flashing the complete console.

## Resize behavior

When the terminal is below the minimum useful dimensions, the UI displays a compact resize message instead of drawing a truncated management page.

A resize invalidates the previous frame and triggers one full redraw at the new dimensions.

## Threading boundary

Only the console thread:

- reads `Console.KeyAvailable`;
- reads `Console.ReadKey`;
- performs differential rendering;
- opens blocking text prompts.

Capture, hotkey, display-topology, and benchmark threads publish immutable state updates. They request a render but never write directly to the console.

## P7 backend controls

The Capture Settings page displays:

- configured backend mode;
- actual active backend;
- visible fallback state and reason when native WIC is unavailable.

Cycling the backend changes future capture requests only. Active and already-pending requests retain their settings snapshot.

The Diagnostics page provides two separate operations:

1. selected-backend baseline;
2. Reference GDI+ versus Native GDI + WIC comparison.

During either benchmark, normal capture requests are rejected so measurements are not contaminated by concurrent capture work.

## P9 capture warnings

The Dashboard keeps a capture in the completed state when its PNG was committed but an optional post-processing action failed.

The last-capture section displays a warning count, while the Status area contains the warning text. Structured warnings are also written to diagnostics when diagnostics are enabled.

Current warning categories:

- storage fallback;
- clipboard publication.
