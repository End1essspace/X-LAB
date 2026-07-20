# SCapturer

SCapturer is a performance-first Windows screenshot developer utility by **XCON**, built under **X-LAB**, with lossless full-desktop capture, a cached-frame rectangular snipping overlay, configurable global hotkeys, diagnostics, and an interactive console management interface.

The application publishes as one C# executable while retaining a reusable Windows capture core. The original Batch/PowerShell proof of concept remains under `legacy/` only as historical reference.

## Current capabilities

- interactive page-based console UI;
- styled differential rendering without continuous `Console.Clear`;
- semantic console colors limited to explicit state fields, severity tokens, hotkeys, and selection;
- fixed-column runtime telemetry, structured last-capture details, and a bounded session event feed;
- context-aware footer controls and page-aware console-window titles;
- configurable global hotkeys with rollback;
- complete physical virtual-desktop capture;
- cached-frame rectangular region capture;
- Per-Monitor V2 and negative-coordinate support;
- display-topology invalidation and safe retry/cancellation;
- reference GDI+ capture backend;
- native GDI `CreateDIBSection` and `BitBlt` backend;
- native WIC PNG persistence from direct BGRA memory;
- atomic same-directory PNG commit after explicit disk flush;
- stale temporary-file cleanup and destination free-space checks;
- automatic fallback when a configured capture folder is unavailable;
- dedicated STA clipboard dispatcher with bounded retry;
- clipboard failures reported as warnings without invalidating saved PNG files;
- backend availability fallback;
- selected-backend baseline benchmark;
- reference/native comparison benchmark with an explicit performance gate;
- separate Full and Snips folders;
- recent-capture browser;
- optional clipboard copy, sound, and diagnostics;
- one bounded STA capture worker;
- graceful overlay and process shutdown;
- hidden background operation without restarting capture services;
- configurable console show/hide hotkey;
- single-instance activation and command forwarding;
- close-button background handoff for the native console window;
- current-user Windows autostart with stale-path detection;
- dependency-free automated logic test suite;
- isolated resource-soak and repeated lifecycle harness.

## Default hotkeys

| Shortcut | Action |
| --- | --- |
| `Ctrl + Shift + G` | Capture the complete virtual desktop |
| `Ctrl + Shift + S` | Open rectangular region selection |
| `Ctrl + Shift + H` | Show or hide the management console |
| `Ctrl + Shift + Q` | Exit after active file work finishes |

Hotkeys can be changed from the **Hotkeys** page.

## Capture backends

### Reference GDI+

The compatibility and rollback implementation uses:

- `System.Drawing.Bitmap`;
- `Graphics.CopyFromScreen`;
- GDI+ crop and PNG persistence.

### Native GDI + WIC

The optimized implementation uses:

- top-down `CreateDIBSection` memory;
- `BitBlt` for desktop acquisition and crop;
- one direct BGRA pixel buffer;
- the Windows Imaging Component PNG encoder through `WritePixels`.

The native buffer is exposed to the overlay and clipboard through a non-owning `Bitmap` view, avoiding a second complete managed frame copy.

## Backend gate

New installations start on **Reference GDI+**.

The Diagnostics page can compare both backends using one warm-up and ten measured full captures per backend. Native is applied only when:

- median total latency does not regress by more than 5%; and
- p95 total latency or managed allocations improve by at least 20%.

The report and decision are saved under:

```text
%LOCALAPPDATA%\SCapturer\diagnostics\benchmarks
```

See [`docs/BACKEND_COMPARISON.md`](docs/BACKEND_COMPARISON.md).

## Console pages

1. Dashboard
2. Capture Settings
3. Hotkeys
4. Save Locations
5. Diagnostics and Benchmark
6. Recent Captures
7. Background and Startup
8. About

Navigation:

- `↑` / `↓` or `J` / `K` — select;
- `Enter` — activate;
- `1`–`9` and `0` — direct visible item;
- `Esc` / `Backspace` — back;
- `R` — refresh Recent Captures;
- `F` — open selected capture folder.

## Background mode and single-instance activation

The application is published as a Windows executable, so background startup does not flash a console window. The management console is allocated lazily on its first show request; later hide/show operations reuse that one attachment and only change window visibility, avoiding repeated standard-handle churn.

Launch hidden:

```powershell
dotnet run --project .\src\SCapturer.App\SCapturer.App.csproj -c Release -- --background
```

The same process continues to own hotkeys, capture, clipboard, display observation, and diagnostics while the console is hidden. Use the configurable console hotkey or launch SCapturer again to show the existing console. A second copy forwards its command through a current-user named pipe and exits.

The native console close button has special Windows semantics: `CTRL_CLOSE_EVENT` always terminates the process after its control handlers run. SCapturer therefore performs a hidden background handoff when `X` is pressed. A replacement instance waits for the closing PID, acquires the normal single-instance mutex, and resumes the listener hidden. Normal menu/hotkey hiding remains an in-process `SW_HIDE` operation with no restart.

Supported executable arguments:

```text
--background
--show
--hide
--toggle-console
--capture-full
--capture-region
--cancel-region
--exit
```

Windows autostart is managed from the **Background and Startup** page and registers the current executable under the current-user Run key with `--background`.

See [`docs/BACKGROUND_AND_AUTOSTART.md`](docs/BACKGROUND_AND_AUTOSTART.md).

## Storage

Full captures:

```text
%USERPROFILE%\Pictures\SCapturer\Full
```

Region captures:

```text
%USERPROFILE%\Pictures\SCapturer\Snips
```

Settings:

```text
%LOCALAPPDATA%\SCapturer\config.json
```

Diagnostics:

```text
%LOCALAPPDATA%\SCapturer\diagnostics
```

## Repository structure

```text
SCapturer.sln
Directory.Build.props
src/
  SCapturer.App/
    Lifecycle/
    UI/
  SCapturer.Core/
    Benchmarking/
    Capture/
    Diagnostics/
    Display/
    Models/
    Persistence/
    Pipeline/
    Services/
    Snipping/
tests/
  SCapturer.Tests/
tools/
  SCapturer.Reliability/
docs/
legacy/
```

## Development requirements

- Windows 11 x64 — primary target;
- Windows 10 version 2004 or newer — best-effort compatibility;
- .NET 8 SDK.

## Build

```powershell
dotnet build .\SCapturer.sln -c Release
```

## Run from source

```powershell
dotnet run --project .\src\SCapturer.App\SCapturer.App.csproj -c Release
```

## Automated verification

Run deterministic logic tests:

```powershell
dotnet run --project .\tests\SCapturer.Tests\SCapturer.Tests.csproj -c Release
```

Run the default isolated Windows reliability workload after a Release build:

```powershell
dotnet run --project .\tools\SCapturer.Reliability\SCapturer.Reliability.csproj -c Release -- --captures 100 --console-cycles 30 --region-cancel-cycles 5 --process-cycles 10
```

The reliability baseline is taken after representative full-capture, IPC, console show/hide, and region-cancellation warm-up so the resource gates measure steady-state growth rather than first-use framework caches.

Reports are written under `artifacts/reliability`. See [`docs/RELIABILITY.md`](docs/RELIABILITY.md).

## Publish as one self-contained EXE

```powershell
dotnet publish .\src\SCapturer.App\SCapturer.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false -o .\dist\SCapturer
```

## Roadmap status

- P0 — standalone C# foundation: complete;
- P1 — product identity and solution normalization: complete;
- P2 — performance metrics and baseline benchmark: complete;
- P3 — bounded asynchronous capture pipeline: complete;
- P4 — high-performance rectangular snipping overlay: complete;
- P5 — mixed-DPI and multi-monitor hardening: complete;
- P6 — interactive console UI v1: complete;
- P7 — native GDI and WIC capture pipeline: complete;
- P8 — GPU backend research gate: deferred unless measurements justify it;
- P9 — atomic PNG persistence and clipboard hardening: complete;
- P10 — background lifecycle and Windows autostart: complete;
- P11 — automated reliability tests and resource-soak validation: complete.
- P12 — developer-console telemetry layout, semantic color, event feed, and close-button lifecycle polish: complete.

Created by **XCON** as part of **X-LAB** — practical automation utilities.
