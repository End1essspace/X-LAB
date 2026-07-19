# SCapturer Performance Baseline and P4 Snipping

## Full capture

The full-capture backend and P2 benchmark remain based on:

- `System.Drawing.Bitmap`;
- `Graphics.CopyFromScreen`;
- the built-in PNG encoder.

Existing measurements remain:

- dispatch;
- directory preparation;
- bitmap allocation;
- pixel acquisition;
- PNG persistence;
- clipboard;
- sound;
- total duration.

## Region capture

P4 adds a separate `SnipCaptureMetrics` block with three measurements:

- **overlay preparation** — dimmed-frame and overlay construction;
- **interaction** — time from overlay presentation to user confirmation;
- **crop** — creation of the selected bitmap from the cached frame.

The common `CaptureMetrics` shape remains unchanged, preserving the P2/P3 full-capture benchmark report schema.

`TotalMilliseconds` includes interaction time. Use the individual fields rather than total duration when comparing capture or encoder performance.

## Cached-frame rule

A region capture performs exactly one physical desktop acquisition before the overlay appears.

During selection:

- no additional `CopyFromScreen` call occurs;
- no desktop bitmap is reallocated;
- the dimmed frame is not regenerated;
- no timer polls the mouse;
- dirty-region repainting follows mouse events.

The saved region is cropped from the original cached frame, so overlay visuals cannot enter the PNG.

## Memory bound

While the overlay is active, P4 holds two 32-bit virtual-desktop images:

```text
virtual width × virtual height × 4 × 2 bytes
```

After confirmation, one selected-region bitmap exists temporarily. All images are disposed before the worker accepts another request.

The shared coordinator still permits only one active request and one pending request, preventing multiple overlay frame sets from existing concurrently.

## Hotkey pressure

Global hotkeys use `MOD_NOREPEAT`. Holding `Ctrl+Shift+G` or `Ctrl+Shift+S` therefore does not generate a continuous stream of requests.

Rapid distinct presses are still bounded by the coalesced pending slot.

## P4 acceptance checks

### Functional

- `Ctrl+Shift+S` opens one overlay over the virtual desktop.
- Left drag saves exactly one PNG.
- `Esc` and right-click create no file.
- Saved pixels contain no dimming, border, or size label.
- Clipboard content matches the saved crop.
- Full and region folders are independent.
- Full capture behavior remains unchanged.

### Responsiveness

- Overlay rendering does not run on the hotkey or console thread.
- Mouse movement does not recapture the desktop.
- No parallel overlays can open.
- At most one later request remains pending.

### Diagnostics

A successful region entry records:

- capture kind;
- absolute virtual-screen rectangle;
- desktop acquisition;
- overlay preparation;
- interaction;
- crop;
- PNG persistence;
- total duration.

### Resource behavior

After repeated captures and cancellations:

- overlay forms are disposed;
- original, dimmed, and crop bitmaps are disposed;
- GDI and USER handles do not grow linearly;
- cancellation creates no PNG;
- shutdown closes an active overlay without further input.

## Benchmark scope

The existing automated benchmark remains a full-desktop benchmark so P2, P3, and P4 results stay comparable. User-controlled selection time is not mixed into that benchmark.

P5 can add deterministic geometry tests and overlay startup measurements without simulating human interaction.
