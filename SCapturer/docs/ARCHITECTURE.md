# SCapturer Architecture

## Current boundary

SCapturer is divided into two projects while publishing as one user-facing executable.

### `SCapturer.App`

Owns application concerns:

- process entry point;
- single-instance mutex;
- console lifecycle and rendering;
- command dispatch;
- application-level status messages;
- asynchronous benchmark presentation;
- composition of core services.

### `SCapturer.Core`

Owns reusable Windows screenshot functionality:

- virtual-desktop capture;
- PNG persistence;
- clipboard integration;
- global hotkey registration;
- capture-stage instrumentation;
- diagnostics persistence;
- baseline benchmark execution and reports;
- bounded capture coordination;
- settings models and storage;
- application data paths.

`SCapturer.Core` currently references Windows Forms because the baseline capture and clipboard implementations use Windows desktop APIs. Later performance work may replace these implementations behind explicit interfaces without changing the application shell.

## Product identity

- executable: `SCapturer.exe`;
- application namespace: `SCapturer.App`;
- core namespace: `SCapturer.Core`;
- mutex: `Local\SCapturer.App`;
- settings: `%LOCALAPPDATA%\SCapturer\config.json`;
- diagnostics: `%LOCALAPPDATA%\SCapturer\diagnostics`;
- default captures: `%USERPROFILE%\Pictures\SCapturer\Full`.

## P3 capture pipeline

`CaptureCoordinator` owns a dedicated background STA thread. All normal screenshots execute on this worker so neither the Win32 hotkey message loop nor the console command loop performs pixel acquisition, PNG encoding, disk writes, or clipboard publication.

The coordinator has two bounded storage layers:

1. one request currently owned by the worker;
2. one pending request slot protected by a lock.

A bounded `Channel<byte>` with capacity one is used as a wake-up signal. The screenshot request itself is held in the pending slot. When another request arrives before that slot is consumed, SCapturer replaces the pending request with the newest request instead of allocating another task or queue node.

This gives the application a strict upper bound:

```text
one active capture + one pending capture
```

The coordinator publishes immutable versioned snapshots. Versioning prevents delayed events from overwriting newer pipeline state in the console shell.

## Pipeline states

The current state model is:

- `Idle`;
- `Queued`;
- `Capturing`;
- `Saving`;
- `Publishing`;
- `Finalizing`;
- `Completed`;
- `Failed`;
- `Stopping`;
- `Stopped`.

`CaptureService` reports stage transitions but remains responsible for the same measured synchronous backend established in P2. This preserves benchmark comparability while moving execution to the dedicated worker.

## Settings snapshots

Every queued request receives an independent `AppSettings` snapshot. Console changes therefore affect future requests without mutating an active or pending capture.

## Diagnostics boundary

`CaptureService` returns immutable metrics. `CaptureDiagnosticsStore` persists optional JSONL entries after the measured capture has completed, so diagnostics I/O is not part of `TotalMilliseconds`.

## Benchmark behavior

The P2 benchmark still invokes the baseline capture service directly to preserve its report semantics. In P3 it runs on a separate task so the console can continue rendering progress. User screenshots are rejected while the benchmark is active, and benchmark cancellation is observed between samples during shutdown.

## Shutdown

Shutdown follows this order:

1. stop accepting new capture requests;
2. complete the bounded signal channel;
3. finish the active capture;
4. process the single already-pending capture;
5. stop the worker;
6. cancel and join the benchmark task.

A screenshot already being encoded is not interrupted, preventing a deliberately requested final image from being abandoned halfway through persistence.

## Next architectural change

P4 adds the rectangular snipping overlay. It will reuse the same coordinator rather than create a second capture worker. The overlay will acquire one cached desktop frame, perform selection rendering without repeated screen captures, and submit the resulting crop through the existing bounded pipeline.
