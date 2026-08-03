# Architecture

SCapturer ships as one Windows executable while keeping the application shell separate from the reusable capture and persistence logic.

The design is intentionally small: one controller loop, one bounded capture worker, one clipboard dispatcher, and explicit ownership of every native or managed resource.

## Solution boundaries

### `SCapturer.App`

The application project owns process-level behavior:

- program startup and Per-Monitor V2 initialization;
- single-instance mutex and command forwarding;
- console allocation, visibility, and close-button handoff;
- page navigation and differential terminal rendering;
- command dispatch and settings orchestration;
- composition and lifetime of core services;
- presentation of diagnostics and benchmark results.

### `SCapturer.Core`

The core project owns Windows capture behavior and persistent application state:

- capture backend interfaces and selection;
- reference GDI+ and native GDI + WIC implementations;
- physical display topology discovery and invalidation;
- full-desktop and rectangular region capture;
- bounded capture coordination;
- atomic PNG persistence;
- clipboard publication;
- global hotkey registration and reconfiguration;
- settings, application paths, diagnostics, and recent captures;
- baseline and backend-comparison benchmarks.

Production code does not reference the test or reliability projects.

## Runtime composition

`Program` creates the application services, starts the single-instance IPC server, and hands control to `AppController`.

`AppController` is the runtime coordinator. It receives console commands, hotkey callbacks, IPC requests, display changes, and capture events. Background services publish state updates but never write directly to the console.

```text
Hotkeys / console / IPC
          │
          ▼
    AppController
          │
          ▼
  CaptureCoordinator
          │
   ┌──────┴──────┐
   ▼             ▼
Full capture   Region capture
   │             │
   └──────┬──────┘
          ▼
 Capture backend
          ▼
 PNG persistence
          ▼
Optional clipboard / diagnostics
```

## Bounded capture pipeline

`CaptureCoordinator` owns one background STA thread. Full and region captures execute on that thread so the hotkey message loop and console loop remain responsive during pixel acquisition, overlay interaction, PNG encoding, disk I/O, and clipboard work.

The queue has a strict upper bound:

```text
one active request + one coalesced pending request
```

When another request arrives while one is already pending, the newest request replaces the previous pending request. The pipeline therefore cannot grow without limit under repeated hotkey input.

Each request receives an immutable settings snapshot. Changing a backend, folder, or option affects future requests only.

## Display topology and DPI

`DisplayTopologyService` is the single source of physical monitor geometry.

The process enables Per-Monitor V2 awareness before any capture or overlay work. Topology snapshots contain:

- complete virtual-desktop bounds;
- monitor bounds and working areas;
- primary-monitor identity;
- remote-session state;
- a monotonically increasing topology version.

This model supports monitors with different scaling factors and monitors positioned at negative coordinates.

Full capture validates the topology version after pixel acquisition and retries once if the display layout changed. Region capture binds one cached frame to one topology version; a topology change closes the overlay and produces no file.

## Capture backend boundary

`ICaptureBackend` defines the operations required by the rest of the application:

- capture a physical desktop rectangle;
- crop an existing frame;
- encode a frame as PNG;
- report availability and backend identity.

`CaptureFrame` exposes dimensions, stride, backend metadata, and a `Bitmap` view used by the WinForms overlay and clipboard integration.

### Reference GDI+

`ReferenceGdiPlusCaptureBackend` provides the compatibility implementation:

- `Bitmap` allocation;
- `Graphics.CopyFromScreen` acquisition;
- GDI+ crop;
- `Bitmap.Save` PNG persistence.

### Native GDI + WIC

`NativeGdiWicCaptureBackend` uses:

- a top-down `CreateDIBSection` BGRA buffer;
- a selected memory device context;
- `BitBlt` for desktop acquisition and region crop;
- an opaque-alpha normalization pass;
- direct WIC `WritePixels` PNG encoding.

The native encoder writes from the frame buffer without creating an intermediate managed byte array.

`CaptureBackendProvider` resolves explicit reference/native modes and `Auto`. When native WIC is unavailable, the provider returns the reference backend with a visible fallback reason.

## Region capture

`SnippingService` captures the virtual desktop once and passes that cached frame to the overlay.

The overlay:

- spans the complete virtual desktop;
- uses physical coordinates;
- dims the cached frame;
- tracks one rectangular selection;
- supports cancellation;
- returns a rectangle relative to the cached frame.

The selected region is cropped from the original frame. The overlay itself is never recaptured.

## Persistence and clipboard

`CapturePersistenceService` owns the complete file transaction:

1. normalize and validate the configured destination;
2. fall back to the default folder when necessary;
3. verify write access and conservative path length;
4. check available disk space;
5. encode to a same-directory temporary file;
6. flush file contents to disk;
7. rename to a unique final `.png` path;
8. remove stale temporary files on later use.

Capture backends only encode to the temporary path supplied by the persistence service.

`ClipboardPublicationService` runs on a separate bounded STA dispatcher. It clones the image before queueing and retries transient Windows clipboard failures with a bounded exponential backoff.

A clipboard failure does not invalidate an already committed PNG. Storage fallback and clipboard errors are returned as structured capture warnings.

## Hotkeys and command routing

`HotkeyService` owns the Windows message loop used for global hotkey registration. Reconfiguration is transactional:

1. validate the complete candidate set;
2. unregister the current set;
3. register every candidate binding;
4. restore the previous set if any registration fails;
5. persist settings only after registration succeeds.

`AppInstanceService` provides current-user named-pipe IPC. A secondary invocation detects the existing instance through the local mutex, forwards one semantic command, and exits. The IPC server only enqueues commands; `AppController` executes them on its controller loop.

## Background lifecycle

`SCapturer.App` is built as `WinExe`, so hidden startup does not flash a console window.

`ConsoleVisibilityService` allocates the console lazily. After the first allocation, normal hide/show operations keep the same process and console attachment and only change window visibility.

The native title-bar close button is different. Windows terminates a console process after `CTRL_CLOSE_EVENT`, even when a handler reports that it handled the event. `ConsoleCloseHandoffService` therefore starts a hidden replacement process, which waits for the closing PID and then enters the normal mutex-controlled startup path.

`AutostartService` manages the current-user Run value and always launches SCapturer with `--background`. It distinguishes disabled, current, stale, and error states.

See [Background and autostart](BACKGROUND_AND_AUTOSTART.md).

## Console boundary

`ConsoleUi` owns:

- active page and retained selection;
- keyboard interpretation;
- blocking text prompts;
- page-aware window titles;
- styled frame construction;
- differential terminal rendering.

The UI renders immutable snapshots supplied by `AppController`. Capture, hotkey, display, and benchmark threads request a redraw but never write to the terminal.

A bounded in-memory event queue feeds the Dashboard. It is session-only and does not replace persistent diagnostics.

See [Console UI](CONSOLE_UI.md).

## Diagnostics and benchmarks

Capture diagnostics are written as JSON Lines when enabled. Benchmark reports are stored separately under the application diagnostics directory.

`BaselineBenchmarkService` measures the currently selected backend. `BackendComparisonBenchmarkService` runs the reference and native implementations under the same workload and applies an explicit recommendation only after the configured gate succeeds.

Normal capture requests are rejected while a benchmark is running so measurements are not mixed with unrelated work.

## Resource ownership

Resource lifetime is deterministic:

- capture and snipping services dispose every `CaptureFrame`;
- native frames release the `Bitmap` view, selected GDI object, bitmap handle, memory device context, and buffer ownership in a fixed order;
- WIC COM objects and file streams are released after each encoding transaction;
- clipboard clones are disposed by the dispatcher;
- the capture worker, hotkey loop, overlay, IPC server, and benchmark tasks participate in graceful shutdown.

These ownership rules are exercised by the external reliability harness.

## Verification boundary

`SCapturer.Tests` is a dependency-free executable test runner for deterministic logic such as hotkey parsing, settings normalization, launch options, persistence collisions, and pipeline state.

`SCapturer.Reliability` starts the real application with an isolated instance suffix and data directory, drives production IPC commands, samples Windows process resources, and writes JSON, JSONL, and Markdown evidence under `artifacts\reliability`.

The harness disables global hotkey registration only for its isolated process to avoid conflicts with a normal SCapturer instance. Capture, persistence, console lifecycle, IPC, and graceful shutdown remain production code paths.

## Deferred GPU backend

A GPU capture backend is intentionally deferred. It should be reconsidered only if measured workloads show that the existing native path cannot meet a concrete latency, allocation, or compatibility requirement.
