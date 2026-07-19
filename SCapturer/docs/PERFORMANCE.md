# SCapturer Performance Baseline

## Purpose

P2 establishes a measurable baseline before the capture path becomes asynchronous or receives a native backend. The current implementation remains based on `System.Drawing.Bitmap`, `Graphics.CopyFromScreen`, and the built-in PNG encoder.

## Capture stages

SCapturer records the following stages for every successful full-desktop capture:

1. **Dispatch** — elapsed time between the caller recording the request and `CaptureService` beginning the operation.
2. **Directory preparation** — destination-directory creation and collision-safe file-name selection.
3. **Bitmap allocation** — allocation of the 32-bit capture bitmap.
4. **Pixel acquisition** — `Graphics.FromImage` and `Graphics.CopyFromScreen` for the entire virtual desktop.
5. **PNG persistence** — PNG encoding, file writing, and final file metadata lookup.
6. **Clipboard** — clipboard image publication including retry delays when the clipboard is busy.
7. **Sound** — system sound dispatch.
8. **Total** — capture-service duration from operation start through optional clipboard and sound work.

Managed allocations are measured with `GC.GetAllocatedBytesForCurrentThread`. Working-set values use `Environment.WorkingSet` before and after the measured operation. These values are diagnostic signals, not exact ownership accounting for native GDI memory.

## Diagnostics mode

When diagnostics are enabled, one compact JSON object is appended per successful user capture to:

```text
%LOCALAPPDATA%\SCapturer\diagnostics\capture-metrics.jsonl
```

Writing the diagnostics entry happens after the measured capture has completed, so log persistence is not included in `TotalMilliseconds`.

## Baseline benchmark

The console benchmark performs:

- one unreported warm-up capture;
- ten measured captures;
- clipboard disabled;
- sound disabled;
- PNG persistence enabled;
- temporary screenshot cleanup after each sample.

The temporary directory is created under the active capture folder so the benchmark exercises the same target volume as normal screenshots:

```text
<FullCaptureFolder>\.scapturer-benchmark
```

The directory is removed when cleanup succeeds. The JSON report is retained under:

```text
%LOCALAPPDATA%\SCapturer\diagnostics\benchmarks
```

The report includes individual samples plus:

- median total duration;
- p95 total duration using nearest-rank selection;
- fastest and slowest total duration;
- median pixel-acquisition duration;
- median PNG-persistence duration;
- average managed allocations;
- average PNG file size;
- operating system, runtime, architecture, processor count, and virtual-desktop dimensions.

## Interpretation

The benchmark is intentionally end-to-end and captures the visible desktop repeatedly. Results change with:

- desktop content complexity, because PNG compression cost varies;
- display resolution and monitor count;
- target drive and filesystem state;
- antivirus scanning;
- background CPU and disk activity;
- Remote Desktop and display-driver state.

Use the same desktop content, save location, release configuration, and machine state when comparing later milestones.

## P3 comparison rule

P3 must preserve this report schema or provide an explicit migration. The asynchronous pipeline is accepted only after comparing the same benchmark profile before and after the change and verifying that responsiveness improves without regressing capture correctness or resource stability.
