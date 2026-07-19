# SCapturer Architecture

## Current boundary

SCapturer publishes as one executable while separating application concerns from reusable Windows capture logic.

### `SCapturer.App`

Owns:

- process entry point and single-instance mutex;
- process-level Per-Monitor V2 initialization;
- console lifecycle and rendering;
- command dispatch and status presentation;
- asynchronous benchmark presentation;
- composition of core services.

### `SCapturer.Core`

Owns:

- native display-topology discovery and invalidation;
- full virtual-desktop capture;
- rectangular snipping;
- PNG persistence and clipboard publication;
- global hotkeys and display-change messages;
- diagnostics and benchmark reports;
- bounded capture coordination;
- settings and application paths.

## Shared STA worker

`CaptureCoordinator` owns one background STA thread. Full and region captures execute there, keeping the hotkey message loop and console loop free from pixel acquisition, overlay interaction, PNG encoding, disk I/O, and clipboard calls.

The coordinator remains strictly bounded:

```text
one active request + one coalesced pending request
```

## P5 display topology boundary

`DisplayTopologyService` is the single source of truth for physical monitor geometry.

It enumerates visible monitors with:

- `EnumDisplayMonitors`;
- `GetMonitorInfo`.

Each immutable snapshot contains:

- a monotonically increasing topology version;
- the union of all physical monitor bounds;
- each monitor device name, bounds, work area, and primary flag;
- local or remote-session state;
- the active Windows Forms DPI mode.

The service intentionally avoids using primary-monitor dimensions as the capture boundary. Negative coordinates are preserved for monitors positioned left of or above the primary display.

## Topology invalidation

The topology generation is invalidated by:

- `SystemEvents.DisplaySettingsChanging`;
- `SystemEvents.DisplaySettingsChanged`;
- `WM_DISPLAYCHANGE` received by the hotkey message window;
- resume from sleep;
- remote and console session transitions.

Refresh is debounced with a one-shot timer. There is no periodic display polling.

Capture requests call `AcquireStableSnapshot`. If Windows is in the middle of a display transition, capture waits for a bounded stabilization interval instead of allocating a bitmap from stale dimensions.

## Full-capture consistency

`CaptureService` records one topology version before allocation and pixel acquisition.

After `CopyFromScreen`, it verifies that the topology is still current. If the version changed, the incomplete in-memory frame is discarded and the service retries once using the new stable topology. A PNG is written only from a topology-consistent frame.

Every successful `CaptureResult` includes `CaptureDesktopContext` so diagnostics can identify:

- topology version;
- monitor count;
- virtual bounds;
- remote-session state;
- DPI mode.

## Snipping consistency

`SnippingService` captures one stable desktop frame and associates it with one topology version.

If display topology changes before or during interaction:

1. the active selection receives a `DisplayTopologyChanged` cancellation reason;
2. the overlay closes on its UI thread;
3. no PNG is created;
4. the cached frame is disposed;
5. the next request acquires a fresh topology.

`SnipOverlayForm` also listens directly for `WM_DISPLAYCHANGE`, providing a second invalidation path even if a higher-level system event is delayed.

## Per-Monitor V2 overlay

The process enables `HighDpiMode.PerMonitorV2` before any form handle exists.

The overlay uses:

- `AutoScaleMode.None`;
- one exact physical virtual-desktop rectangle;
- `SetWindowPos` with negative-coordinate support;
- `WM_DPICHANGED` handling that restores the exact physical bounds instead of accepting a DPI-scaled suggested rectangle.

Its client coordinates therefore map directly to cached-frame pixel coordinates.

## Cancellation reasons

Region cancellation is explicit:

- `User` — `Esc`, right-click, or an invalid tiny selection;
- `DisplayTopologyChanged` — stale geometry or cached frame;
- `Shutdown` — process exit while selection is active.

This distinction is propagated through `CaptureCancelledEvent` to the console UI.

## Next architectural change

P6 replaces the milestone-oriented console menu with the complete interactive console management UI, including structured settings pages, hotkey editing, recent captures, and non-flickering status updates.
