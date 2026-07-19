# SCapturer Architecture

## Current boundary

SCapturer publishes as one executable while separating application concerns from reusable Windows capture logic.

### `SCapturer.App`

Owns:

- process entry point and single-instance mutex;
- process-level Per-Monitor V2 initialization;
- interactive console lifecycle;
- page navigation and differential rendering;
- command dispatch and settings orchestration;
- backend comparison presentation and recommendation persistence;
- composition of core services.

### `SCapturer.Core`

Owns:

- capture backend interfaces and selection;
- reference GDI+ capture and persistence;
- native GDI frame allocation and WIC PNG encoding;
- native display-topology discovery and invalidation;
- rectangular snipping;
- clipboard publication;
- global hotkey registration and live reconfiguration;
- diagnostics, baseline reports, and backend comparison reports;
- recent-capture discovery;
- bounded capture coordination;
- settings and application paths;
- atomic PNG persistence and clipboard publication.

## Shared STA worker

`CaptureCoordinator` owns one background STA thread. Full and region captures execute there, keeping the hotkey message loop and console loop free from pixel acquisition, overlay interaction, PNG encoding, disk I/O, and clipboard calls.

The coordinator remains strictly bounded:

```text
one active request + one coalesced pending request
```

## P7 backend boundary

`ICaptureBackend` defines:

- physical desktop capture;
- frame crop;
- PNG persistence;
- backend availability;
- immutable backend identity.

`CaptureFrame` exposes common dimensions and one `Bitmap` view required by the existing WinForms overlay and clipboard integration.

### Reference backend

`ReferenceGdiPlusCaptureBackend` preserves the previous implementation:

- `Bitmap` allocation;
- `Graphics.CopyFromScreen`;
- GDI+ crop;
- `Bitmap.Save` PNG persistence.

### Native backend

`NativeGdiWicCaptureBackend` uses:

- top-down `CreateDIBSection` storage;
- one selected memory device context;
- `BitBlt` for desktop acquisition and region crop;
- an opaque BGRA normalization pass;
- direct WIC `WritePixels` PNG encoding.

The WIC path writes the native buffer directly to the system PNG encoder. It does not create an intermediate managed byte array.

## Backend selection

`CaptureBackendProvider` resolves:

- `ReferenceGdiPlus`;
- `NativeGdiWic` with visible fallback when unavailable;
- `Auto`, which prefers native and falls back to reference.

New settings default to reference. `BackendComparisonBenchmarkService` runs both implementations and persists the recommended explicit mode only after the performance gate succeeds.

## Display topology consistency

`DisplayTopologyService` remains the single source of physical monitor geometry.

Full capture validates the topology version after backend acquisition and retries once if geometry changed.

Region capture associates one backend frame with one topology version. Any topology change closes the overlay and creates no file.

## Console boundary

`ConsoleUi` owns page state, selection, prompts, and differential terminal rendering.

The Capture Settings page exposes backend mode and actual active backend. The Diagnostics page exposes selected-backend baseline and reference/native comparison.

Background services never write directly to the terminal.

## Resource boundary

Every `CaptureFrame` is disposed by the capture or snipping service.

The native frame releases managed, GDI, and memory resources in deterministic order. WIC COM objects and file streams are released after each encoder transaction.

## P9 persistence boundary

`CapturePersistenceService` owns destination validation, fallback selection, free-space checks, temporary-file cleanup, encoding transactions, disk flush, and final same-directory rename. Capture backends only encode to the path supplied by this service.

`ClipboardPublicationService` owns a separate bounded STA dispatcher. It clones the image before queueing and performs exponential retry without allowing clipboard errors to invalidate a committed PNG.

`CaptureResult` can carry structured warnings for storage fallback and clipboard publication. Diagnostics and the console expose these warnings while treating the capture itself as completed.

## GPU research decision

P8 remains a conditional research gate. It is deferred unless measured P7 workload data demonstrates that another backend is likely to provide material value.

## Next architectural change

P10 adds background lifecycle, single-instance activation, console show/hide, and Windows autostart.
