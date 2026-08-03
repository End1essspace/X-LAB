# Performance and Benchmarking

SCapturer measures the complete screenshot path rather than only the screen-copy call. The goal is to compare capture backends under the same persistence contract and to detect regressions in latency, allocations, and long-running resource use.

## Capture backends

Both implementations satisfy the same `ICaptureBackend` contract.

### Reference GDI+

The reference backend uses:

- a managed `Bitmap` frame;
- `Graphics.CopyFromScreen` for desktop acquisition;
- GDI+ drawing for region crops;
- the GDI+ PNG encoder.

It is always available and serves as the compatibility baseline.

### Native GDI + WIC

The native backend uses:

- a top-down 32-bit `CreateDIBSection`;
- a dedicated memory device context;
- `BitBlt` for desktop acquisition and region crops;
- Windows Imaging Component for PNG encoding through `WritePixels`.

A temporary managed `Bitmap` view references the same DIB memory for overlay and clipboard boundaries. The full frame is not copied into a second managed bitmap.

Availability is probed through WIC initialization. When the native backend cannot be created, SCapturer falls back visibly to the reference backend.

## Opaque alpha normalization

Desktop pixels copied into a 32-bit GDI DIB do not provide a reliable alpha byte. The native backend therefore sets every pixel's alpha channel to fully opaque before the frame is used by the overlay, clipboard, or PNG encoder.

The normalization pass preserves RGB values and is included in pixel-acquisition timing.

## Per-capture metrics

Each completed capture records:

| Metric | Scope |
| --- | --- |
| Dispatch | Delay between the request timestamp and worker execution |
| Directory preparation | Path normalization, creation, validation, and destination allocation |
| Buffer allocation | Backend frame allocation |
| Pixel acquisition | Desktop copy, including native alpha normalization |
| PNG persistence | Encoding, validation, disk flush, and atomic rename |
| Clipboard | Optional clipboard publication |
| Sound | Optional capture-sound dispatch |
| Total | End-to-end capture duration |
| Managed allocations | Bytes allocated on the capture worker thread |
| Working set | Process working set before and after capture |

The capture result also records the effective backend, dimensions, file size, topology context, and warnings. Benchmark reports additionally record the requested backend mode.

## Selected-backend baseline

The baseline benchmark runs the backend currently selected in settings.

Default workload:

```text
1 warm-up capture
10 measured captures
```

Benchmark captures:

- use the normal production capture and persistence path;
- disable clipboard publication;
- disable capture sound;
- disable per-capture diagnostics;
- are deleted after each sample on a best-effort basis.

The JSON report uses schema version `2.0` and includes the operating system, runtime, process architecture, processor count, dimensions, backend identity, individual samples, and summary statistics.

Reports are written under:

```text
%LOCALAPPDATA%\SCapturer\diagnostics\benchmarks
```

## Backend comparison

The comparison benchmark runs the same workload for both implementations:

```text
Reference GDI+: 1 warm-up + 10 measured captures
Native GDI + WIC: 1 warm-up + 10 measured captures
```

Both sides use the same settings and target volume.

The comparison calculates:

```text
p95 improvement =
    (reference p95 - native p95) / reference p95 × 100

allocation improvement =
    (reference average allocations - native average allocations)
    / reference average allocations × 100
```

The native backend is recommended only when both conditions hold:

1. its median total duration does not regress by more than 5%;
2. either p95 total duration or average managed allocations improves by at least 20%.

Otherwise the reference backend remains selected. A successful comparison persists the recommended mode to settings and writes a report containing both sample sets and the decision reason.

## Summary statistics

The benchmark reports:

- median total duration;
- p95 total duration;
- fastest and slowest total duration;
- median pixel-acquisition duration;
- median PNG-persistence duration;
- average managed allocations;
- average file size.

For small local runs, p95 is calculated with the nearest-rank method. With ten samples, it therefore represents the slowest sample. Use longer external runs when a more stable tail-latency estimate is required.

## Persistence cost

`PngPersistenceMilliseconds` includes the complete durability boundary:

1. PNG encoding;
2. non-empty temporary-file validation;
3. explicit disk flush;
4. same-directory atomic rename.

This makes benchmark results directly comparable with production captures, but it also means storage performance can dominate total latency.

Destination validation is cached per folder. Stale temporary-file cleanup runs once per folder, and the local free-space check is constant-time.

## Bounded execution model

SCapturer permits:

```text
one active request + one coalesced pending request
```

A new request replaces the existing pending request with the latest settings and trigger. The application does not start parallel capture workers, PNG encoders, or region overlays.

Clipboard publication uses a separate STA dispatcher with a queue capacity of one. Single-instance IPC blocks asynchronously until another invocation connects.

When the console is hidden, terminal rendering and keyboard polling stop; the management loop moves from a 40 ms to a 200 ms cadence. Capture and lifecycle services remain event-driven.

## Native resource ownership

Each native frame owns:

- one memory device context;
- one selected DIB section;
- one direct pixel pointer;
- one non-owning managed `Bitmap` view.

Disposal order is fixed:

1. dispose the managed view;
2. restore the previous selected GDI object;
3. delete the DIB section;
4. delete the memory device context.

WIC stream, encoder, frame encoder, and encoder-option COM references are released after every persistence operation.

## Reliability resource gates

The Windows reliability harness samples the process after one-time subsystems have been warmed up. This prevents normal .NET, console, WinForms, GDI, and WIC initialization from being misclassified as leaks.

Default maximum final growth:

| Resource | Gate |
| --- | ---: |
| GDI objects | `+8` |
| USER objects | `+8` |
| Process handles | `+32` |
| Threads | `+3` |
| Private memory | greater of `48 MB` or `20%` of baseline |
| Working set | greater of `64 MB` or `30%` of baseline |

The harness also requires:

- no capture timeouts;
- no failed IPC commands;
- no unexpected region PNG after cancellation;
- no remaining `.scapturer.tmp` files;
- graceful exit for every repeated process cycle.

See [Reliability validation](RELIABILITY.md) for the complete workload and commands.

## Interpreting benchmark results

For useful comparisons:

- keep the monitor topology and resolution unchanged;
- use the same destination volume;
- avoid unrelated disk-heavy or GPU-heavy work;
- compare reports produced by the same build configuration;
- treat p95 and resource growth as more important than one unusually fast sample;
- rerun after changes to persistence, capture buffers, DPI handling, or lifecycle code.

A faster screen copy alone is not enough to justify a backend change. The selected implementation must improve measured end-to-end behavior without weakening capture correctness, topology handling, persistence, or resource stability.
