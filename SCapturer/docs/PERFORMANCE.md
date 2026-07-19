
# SCapturer Performance Baseline and P3 Pipeline

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

## P3 asynchronous execution

P3 preserves the P2 `CaptureService` and benchmark report schema. The measured stages therefore remain directly comparable with the previous milestone.

Normal screenshots now run on one dedicated STA worker. Dispatch latency includes time spent waiting behind an active capture when a request occupies the single pending slot. This is intentional: it exposes user-visible queue delay rather than hiding it.

The coordinator allows at most:

```text
one active capture + one coalesced pending capture
```

Repeated requests replace only the pending request. They never start parallel PNG encoders and never create an unbounded task backlog.

The baseline benchmark still calls the measured capture service directly with clipboard and sound disabled. It runs outside the console loop in P3, but user captures are excluded while the benchmark is active so the samples are not contaminated by concurrent capture work.

## P3 acceptance checks

Compare a P2 report and a P3 report under the same desktop content and save volume. P3 is acceptable when:

- median and p95 capture-service duration remain within normal run-to-run variance;
- hotkey message processing remains responsive during PNG persistence;
- console menu input remains responsive during normal captures;
- rapid hotkey presses produce no more than one active and one pending capture;
- memory does not grow with the number of rejected or coalesced requests;
- shutdown finishes an active file operation without producing a partial final screenshot.

P4 will extend this document with overlay latency and cached-frame selection measurements.
