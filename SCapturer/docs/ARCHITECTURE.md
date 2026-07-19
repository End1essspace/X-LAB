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
- benchmark progress presentation;
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

## P2 diagnostics boundary

`CaptureService` owns measurement of the synchronous capture stages and returns immutable `CaptureMetrics` with every successful `CaptureResult`.

`CaptureDiagnosticsStore` is separate from `CaptureService`. This keeps optional JSONL persistence outside the measured operation and prevents diagnostics policy from becoming part of the capture backend.

`BaselineBenchmarkService` invokes the same public capture path used by normal screenshots. It changes only the benchmark settings profile by disabling clipboard and sound and redirecting output to a temporary directory on the selected capture volume.

## Threading status

The capture path remains synchronous in P2. A hotkey capture currently executes on the hotkey message-loop thread, while a console capture executes on the console thread. P3 will replace this behavior with a bounded capture coordinator and a dedicated worker so UI and Win32 message processing are never blocked by PNG encoding or disk I/O.

## Next architectural change

P3 introduces:

- a bounded capture-request channel;
- one dedicated capture worker;
- one active request plus one coalesced pending request;
- cancellation-aware shutdown;
- non-blocking hotkey and console dispatch;
- explicit capture state transitions.
