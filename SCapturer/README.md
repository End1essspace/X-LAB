# SCapturer

SCapturer is a performance-first Windows screenshot utility with lossless full-desktop capture, a cached-frame rectangular snipping overlay, configurable global hotkeys, diagnostics, and an interactive console management interface.

The application publishes as one C# executable while retaining a reusable Windows capture core. The original Batch/PowerShell proof of concept remains under `legacy/` only as historical reference.

## Current capabilities

- interactive page-based console UI;
- differential rendering without continuous `Console.Clear`;
- configurable global hotkeys with rollback;
- complete physical virtual-desktop capture;
- cached-frame rectangular region capture;
- Per-Monitor V2 and negative-coordinate support;
- display-topology invalidation and safe retry/cancellation;
- reference GDI+ capture backend;
- native GDI `CreateDIBSection` and `BitBlt` backend;
- native WIC PNG encoding from direct BGRA memory;
- atomic same-directory PNG commit after explicit disk flush;
- stale temporary-file cleanup and destination free-space checks;
- automatic fallback when a configured capture folder is unavailable;
- dedicated STA clipboard dispatcher with bounded exponential retry;
- clipboard failures reported as warnings without invalidating saved PNG files;
- backend availability fallback;
- selected-backend baseline benchmark;
- reference/native comparison benchmark with an explicit performance gate;
- separate Full and Snips folders;
- recent-capture browser;
- optional clipboard copy, sound, and diagnostics;
- one bounded STA capture worker;
- graceful overlay and process shutdown.

## Default hotkeys

| Shortcut | Action |
| --- | --- |
| `Ctrl + Shift + G` | Capture the complete virtual desktop |
| `Ctrl + Shift + S` | Open rectangular region selection |
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
7. About

Navigation:

- `↑` / `↓` or `J` / `K` — select;
- `Enter` — activate;
- `1`–`9` — direct visible item;
- `Esc` / `Backspace` — back;
- `R` — refresh Recent Captures;
- `F` — open selected capture folder.

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

PNG persistence uses a temporary file in the destination directory, flushes it to disk, then renames it to the final `.png` name. A crash or encoder failure cannot expose a partial screenshot under its final name.

If a configured capture folder cannot be created or written, SCapturer saves to the corresponding default folder and records a warning. Clipboard publication is independent: a locked clipboard never deletes or invalidates an already committed PNG.

See [`docs/STORAGE_AND_CLIPBOARD.md`](docs/STORAGE_AND_CLIPBOARD.md).

## Repository structure

```text
SCapturer.sln
Directory.Build.props
src/
  SCapturer.App/
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
- P9 — atomic PNG persistence and clipboard hardening: complete in this milestone;
- P10 — background lifecycle and Windows autostart: next.

Part of **X-LAB** — practical automation utilities.
