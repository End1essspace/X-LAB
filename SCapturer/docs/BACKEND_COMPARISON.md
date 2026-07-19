# SCapturer Capture Backend Comparison

## Purpose

P7 introduces two complete screenshot backends behind the same capture contract.

### Reference GDI+

- `System.Drawing.Bitmap` allocation;
- `Graphics.CopyFromScreen` pixel acquisition;
- GDI+ PNG persistence through `Bitmap.Save`.

### Native GDI + WIC

- top-down 32-bit `CreateDIBSection` buffer;
- direct desktop transfer through `BitBlt`;
- direct crop through memory-DC `BitBlt`;
- Windows Imaging Component PNG encoding through `IWICBitmapEncoder` and `IWICBitmapFrameEncode::WritePixels`.

Both backends retain the existing physical display-topology and bounded-worker rules.

## Backend modes

SCapturer stores one of three modes:

- `ReferenceGdiPlus` — always use the reference implementation;
- `NativeGdiWic` — use native when available, otherwise expose a visible reference fallback;
- `Auto` — prefer native when WIC is available, otherwise use reference.

New installations begin on `ReferenceGdiPlus`. The native path is not made primary until the comparison gate is executed.

## Comparison procedure

The Diagnostics page can run a backend comparison.

For each backend it performs:

- one warm-up full-desktop capture;
- ten measured full-desktop captures;
- clipboard disabled;
- capture sound disabled;
- PNG persistence enabled;
- temporary PNG cleanup after every sample.

Both series use the same active display topology and target volume.

The report is written to:

```text
%LOCALAPPDATA%\SCapturer\diagnostics\benchmarks\backend-comparison_*.json
```

## Decision gate

Native is recommended only when all of the following are true:

1. native median total latency does not regress by more than 5%;
2. native improves either p95 total latency or managed allocations by at least 20%.

When native passes, SCapturer persists `NativeGdiWic` as the selected mode.

When native does not pass, SCapturer persists `ReferenceGdiPlus`.

The report always retains both sample sets and the exact decision reason.

## Correctness checks

Performance alone is not sufficient. Validate both backends for:

- exact PNG dimensions;
- pixel-aligned region selection;
- negative virtual-screen coordinates;
- mixed-DPI monitor layouts;
- topology cancellation during snipping;
- clipboard equivalence;
- no alpha transparency in saved PNG files;
- no GDI, USER, COM, or file-handle growth.

## Rollback

The Capture Settings page allows immediate backend switching. The reference backend remains compiled into the executable and does not depend on WIC availability.
