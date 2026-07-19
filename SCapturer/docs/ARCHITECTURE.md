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
- composition of core services.

### `SCapturer.Core`

Owns reusable Windows screenshot functionality:

- virtual-desktop capture;
- PNG persistence;
- clipboard integration;
- global hotkey registration;
- settings models and storage;
- application data paths.

`SCapturer.Core` currently references Windows Forms because the baseline capture and clipboard implementations use Windows desktop APIs. Later performance work may replace parts of this implementation behind explicit interfaces without changing the application shell.

## Product identity

- executable: `SCapturer.exe`;
- application namespace: `SCapturer.App`;
- core namespace: `SCapturer.Core`;
- mutex: `Local\SCapturer.App`;
- current settings: `%LOCALAPPDATA%\SCapturer\config.json`;
- default captures: `%USERPROFILE%\Pictures\SCapturer\Full`.

## Legacy settings migration

On first launch after the rename, the settings store checks the previous path:

```text
%LOCALAPPDATA%\X-LAB\ScreenCaptureTool\config.json
```

When that file contains valid settings, SCapturer writes an equivalent configuration to the new path. The legacy file is retained so migration is non-destructive.

## Next architectural change

P2 introduces capture-stage metrics and a benchmark harness before the capture path becomes asynchronous. This preserves a trustworthy baseline for measuring later performance improvements.
