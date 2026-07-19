# SCapturer

SCapturer is a performance-first Windows screenshot utility with global hotkeys, lossless PNG persistence, an interactive console management interface, built-in diagnostics, a bounded asynchronous capture pipeline, and a native rectangular snipping overlay.

The active implementation is a standalone C# application split into an executable shell and a reusable Windows capture core. The original Batch/PowerShell prototype remains under `legacy/` for historical reference only.

## Current capabilities

- interactive console UI;
- single-instance process guard;
- global Windows hotkeys through `RegisterHotKey`;
- lossless PNG capture of the complete physical virtual desktop;
- rectangular region capture from one cached desktop frame;
- native monitor enumeration through `EnumDisplayMonitors` and `GetMonitorInfo`;
- Per-Monitor V2 DPI awareness;
- mixed-DPI and negative-coordinate support;
- automatic display-topology invalidation;
- safe response to monitor connect/disconnect, resolution changes, sleep/resume, and remote-session transitions;
- separate configurable folders for full captures and snips;
- optional clipboard copy and capture sound;
- persistent JSON settings;
- per-stage timings and optional JSON Lines diagnostics;
- repeatable full-capture baseline benchmark;
- one dedicated STA capture worker;
- one active capture plus one coalesced pending request;
- graceful shutdown that cancels an active overlay.

## Current hotkeys

| Shortcut | Action |
| --- | --- |
| `Ctrl + Shift + G` | Queue capture of the complete virtual desktop |
| `Ctrl + Shift + S` | Open the rectangular snipping overlay |
| `Ctrl + Shift + Q` | Exit SCapturer after active file work finishes |

Hotkeys use `MOD_NOREPEAT`, so holding a combination does not continuously enqueue requests.

## Display and DPI model

SCapturer enables `HighDpiMode.PerMonitorV2` before creating any Windows Forms handles.

The capture rectangle is not derived from a primary-monitor size or a DPI-virtualized system metric. `DisplayTopologyService` enumerates every visible monitor with native Win32 APIs and builds one physical-pixel virtual desktop rectangle from their monitor bounds.

The console displays the current topology, for example:

```text
Displays        : 2 | PerMonitorV2 | LOCAL
Virtual desktop : 4480×1440 @ (-1920,0) | v4
```

A topology version is attached to every successful capture. If Windows changes the display configuration during pixel acquisition, full capture retries once with a fresh topology. If the display changes while the snipping overlay is active, the cached frame is invalidated and selection is cancelled instead of saving incorrect coordinates.

## Rectangular snipping

Region capture follows this pipeline:

1. acquire a stable physical display-topology snapshot;
2. capture the complete virtual desktop once;
3. create one dimmed cached frame;
4. display a topmost overlay across the exact physical virtual desktop;
5. update only selection rendering while the mouse moves;
6. crop the selected rectangle from the original cached frame;
7. save the crop as lossless PNG;
8. optionally publish it to the clipboard.

The desktop is not captured again while the selection rectangle moves. The saved PNG never contains dimming, borders, or the size label.

Controls:

- left mouse drag — select a rectangle;
- `Esc` — cancel;
- right mouse button — cancel.

## Bounded capture pipeline

Full and region requests share one dedicated STA worker:

```text
one active capture + one coalesced pending capture
```

Repeated requests replace only the pending request. SCapturer never starts parallel overlays or parallel PNG encoders.

## Default storage

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
  SCapturer.Core/
    Benchmarking/
    Diagnostics/
    Display/
    Models/
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
dotnet run --project .\src\SCapturer.App\SCapturer.App.csproj
```

## Publish as a self-contained single EXE

```powershell
dotnet publish .\src\SCapturer.App\SCapturer.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false -o .\dist\SCapturer
```

## Roadmap status

- P0 — standalone C# foundation: complete;
- P1 — product identity and solution normalization: complete;
- P2 — performance metrics and baseline benchmark: complete;
- P3 — bounded asynchronous capture pipeline: complete;
- P4 — high-performance rectangular snipping overlay: complete;
- P5 — mixed-DPI and multi-monitor hardening: complete in this milestone;
- P6 — interactive console UI v1: next.

See [`docs/DISPLAY_TEST_MATRIX.md`](docs/DISPLAY_TEST_MATRIX.md) for the required display validation matrix.

Part of **X-LAB** — practical automation utilities.
