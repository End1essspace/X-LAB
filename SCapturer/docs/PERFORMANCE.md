# SCapturer Performance Baseline and P5 Display Hardening

## Stable baseline

The full-capture backend remains based on:

- `System.Drawing.Bitmap`;
- `Graphics.CopyFromScreen`;
- the built-in PNG encoder.

The P2 benchmark report schema remains compatible through P5.

## Existing measurements

Full capture records:

- dispatch;
- directory preparation;
- bitmap allocation;
- pixel acquisition;
- PNG persistence;
- clipboard publication;
- sound dispatch;
- total duration;
- managed allocations on the capture worker;
- working set before and after capture.

Region capture additionally records:

- overlay preparation;
- user interaction;
- crop duration.

## P5 topology acquisition

Monitor topology is event-driven and cached. Normal idle operation performs no monitor polling.

A capture obtains the latest stable immutable snapshot. During a display transition it waits in short bounded intervals for the one-shot refresh to complete. This wait is visible in dispatch and total latency rather than hidden from diagnostics.

## Full-capture retry budget

When topology changes during physical pixel acquisition:

- the first in-memory bitmap is disposed;
- no PNG is written;
- a fresh topology is acquired;
- capture retries once.

The two-attempt ceiling prevents an endless loop when a display driver or remote session is repeatedly reconfiguring the desktop.

Allocation and pixel-acquisition metrics accumulate across both attempts. This exposes the real cost of a topology interruption.

## Snipping invalidation

A cached snipping frame is valid only for its captured topology version.

Any display invalidation during selection closes the overlay and produces no PNG. SCapturer does not attempt to resize or reinterpret the stale frame because that would create incorrect region coordinates or stretched pixels.

## Overlay DPI behavior

The overlay has no render timer and performs no repeated screen acquisition.

`WM_DPICHANGED` may be delivered when Windows changes the DPI associated with the spanning top-level window. The overlay restores its exact physical virtual-desktop rectangle with `SetWindowPos`, keeping the mapping:

```text
one overlay client pixel = one cached desktop pixel
```

## Memory behavior

While region selection is active, SCapturer holds:

```text
virtual width × virtual height × 4 × 2 bytes
```

for the original and dimmed frames. A selected-region bitmap exists only after confirmation.

A topology cancellation disposes both full-desktop frames before the worker accepts another request.

## P5 acceptance checks

P5 is accepted when:

- a 100% and 150% mixed-DPI setup produces pixel-aligned snips;
- monitors left of or above primary retain negative absolute coordinates;
- changing primary display does not offset saved regions;
- changing resolution during selection cancels without creating a file;
- disconnecting a monitor during selection cancels safely;
- full capture after topology change uses the new combined dimensions;
- sleep/resume and Remote Desktop transitions do not require process restart;
- console topology information updates without periodic polling;
- repeated display changes do not leak forms, timers, GDI handles, or event subscriptions.

See `DISPLAY_TEST_MATRIX.md` for the manual validation sequence.

## P6 comparison rule

P6 must not change capture backend timings. Console rendering work must remain outside the capture worker, and UI improvements must not introduce idle polling beyond the existing bounded console input loop.
