# SCapturer Architecture

## Current boundary

SCapturer publishes as one executable while separating application concerns from reusable Windows capture logic.

### `SCapturer.App`

Owns:

- process entry point and single-instance mutex;
- process-level Per-Monitor V2 initialization;
- interactive console lifecycle;
- page navigation and differential rendering;
- command dispatch and text prompts;
- settings orchestration;
- asynchronous benchmark presentation;
- composition of core services.

### `SCapturer.Core`

Owns:

- native display-topology discovery and invalidation;
- full virtual-desktop capture;
- rectangular snipping;
- PNG persistence and clipboard publication;
- global hotkey registration and live reconfiguration;
- hotkey parsing and validation;
- diagnostics and benchmark reports;
- recent-capture filesystem discovery;
- bounded capture coordination;
- settings and application paths.

## Shared STA worker

`CaptureCoordinator` owns one background STA thread. Full and region captures execute there, keeping the hotkey message loop and console loop free from pixel acquisition, overlay interaction, PNG encoding, disk I/O, and clipboard calls.

The coordinator remains strictly bounded:

```text
one active request + one coalesced pending request
```

## P6 console boundary

`ConsoleUi` owns:

- the current page;
- independent selection state for each page;
- menu construction;
- keyboard navigation;
- text prompts;
- differential terminal rendering.

`AppController` owns:

- command execution;
- mutable application settings;
- hotkey reconfiguration transactions;
- recent-capture state;
- capture and benchmark status;
- navigation requests.

Background services never write to the terminal. They update immutable state and request a render through `AppController`.

## Differential terminal rendering

The UI constructs a complete logical frame for every update, then compares it with the previous normalized frame.

Only changed lines are written through `Console.SetCursorPosition`.

A complete `Console.Clear` occurs only for:

- page transitions;
- text prompts;
- terminal-size changes;
- cursor-control recovery.

This prevents the capture state, benchmark progress, and status message from flashing the complete console.

## Hotkey configuration

Hotkeys are stored as structured settings:

- modifier flags;
- Windows virtual-key code.

`HotkeyBindingService` parses display strings, validates modifier and primary-key requirements, formats bindings, and rejects duplicate SCapturer actions.

`HotkeyService` owns the hidden Win32 message window. Reconfiguration runs synchronously on that window's STA thread.

The transaction is:

1. validate the complete candidate set;
2. unregister the current set;
3. register every candidate binding;
4. keep the candidate only if every registration succeeds;
5. otherwise unregister partial candidates and restore the previous set.

Settings are persisted only after the Windows registration transaction succeeds.

## Recent captures

`RecentCaptureService` scans only the configured top-level Full and Snips folders for PNG files.

It returns a bounded newest-first list and tolerates files or folders disappearing during enumeration.

A successful capture is inserted directly into the in-memory recent list so the capture worker does not rescan the filesystem.

## Display topology boundary

`DisplayTopologyService` remains the single source of physical monitor geometry. It uses `EnumDisplayMonitors` and `GetMonitorInfo`, preserves negative coordinates, and invalidates cached geometry through display, power, and session events.

## Capture consistency

Full capture verifies the topology version after pixel acquisition and retries once if geometry changed.

Region capture associates one cached frame with one topology version. Any topology change closes the overlay and creates no file.

## Next architectural change

P7 introduces a native GDI capture buffer and Windows Imaging Component PNG encoder behind explicit backend interfaces. The P2 benchmark and P6 console responsiveness must remain comparable.
