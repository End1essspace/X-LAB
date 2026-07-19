# SCapturer Architecture

## Current boundary

SCapturer publishes as one executable but keeps application concerns and reusable Windows capture logic in separate projects.

### `SCapturer.App`

Owns:

- process entry point and single-instance mutex;
- console lifecycle and rendering;
- command dispatch and application status;
- asynchronous benchmark presentation;
- composition of core services.

### `SCapturer.Core`

Owns:

- full virtual-desktop capture;
- rectangular snipping;
- PNG persistence and clipboard publication;
- global hotkeys;
- diagnostics and benchmark reports;
- bounded capture coordination;
- settings and application paths.

## Product identity

- executable: `SCapturer.exe`;
- mutex: `Local\SCapturer.App`;
- settings: `%LOCALAPPDATA%\SCapturer\config.json`;
- diagnostics: `%LOCALAPPDATA%\SCapturer\diagnostics`;
- full captures: `%USERPROFILE%\Pictures\SCapturer\Full`;
- region captures: `%USERPROFILE%\Pictures\SCapturer\Snips`.

## Shared STA worker

`CaptureCoordinator` owns one background STA thread. Full and region captures both execute there, keeping the hotkey message loop and console loop free from pixel acquisition, overlay interaction, PNG encoding, disk I/O, and clipboard calls.

The coordinator remains strictly bounded:

```text
one active request + one coalesced pending request
```

The pending slot is replaced by the newest request. Full and region requests never create parallel capture workers.

## P4 snipping pipeline

`SnippingService` performs:

1. virtual-desktop bounds lookup;
2. destination preparation;
3. one full-desktop bitmap allocation;
4. one `CopyFromScreen` operation;
5. cached overlay construction;
6. rectangular selection;
7. crop from the original cached frame;
8. PNG persistence;
9. optional clipboard and sound publication.

`SnipOverlayForm` never captures the screen. It owns:

- the original desktop frame supplied by `SnippingService`;
- one pre-dimmed copy;
- selection geometry and a size label.

Mouse movement repaints only the union of the previous and current selection areas and labels. There is no render timer and no repeated desktop acquisition.

## Coordinates

The overlay uses client coordinates relative to the cached virtual desktop. A completed result stores absolute virtual-screen coordinates by adding `SystemInformation.VirtualScreen.Left` and `.Top`.

This retains negative coordinates for monitors positioned left of or above the primary display.

## Pipeline states

P4 adds:

- `PreparingOverlay`;
- `Selecting`;
- `Cropping`;
- `Cancelled`.

These extend the existing queued, capturing, saving, publishing, finalizing, completed, failed, stopping, and stopped states.

## Cancellation and shutdown

`Esc` and right-click cancel region selection without creating a file.

During process shutdown:

1. new requests stop being accepted;
2. the not-yet-started pending request is discarded;
3. an active snipping overlay is closed through its UI thread;
4. an active file write is allowed to finish;
5. the worker exits.

Shutdown therefore never opens a new interactive overlay after the user has requested exit.

## Settings snapshots

Every queued request receives an independent snapshot containing:

- full capture folder;
- snip capture folder;
- clipboard policy;
- sound policy;
- diagnostics policy.

Console changes cannot mutate active or pending capture work.

## Diagnostics compatibility

The common `CaptureMetrics` record remains unchanged, preserving the P2/P3 full-capture benchmark schema. Region-only timings are stored in a separate optional `SnipCaptureMetrics` record attached to `CaptureResult`.

## Next architectural change

P5 hardens physical coordinate behavior for mixed DPI, monitor topology changes, display connect/disconnect, sleep/resume, and Remote Desktop.
