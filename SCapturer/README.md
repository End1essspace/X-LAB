# SCapturer

SCapturer is a performance-first Windows screenshot utility with global hotkeys, lossless PNG persistence, an interactive console management interface, built-in diagnostics, a bounded asynchronous capture pipeline, and a native rectangular snipping overlay.

The active implementation is a standalone C# application split into an executable shell and a reusable core library. The original Batch/PowerShell prototype remains under `legacy/` for historical reference only.

## Current capabilities

- interactive console UI;
- single-instance process guard;
- global Windows hotkeys through `RegisterHotKey`;
- lossless PNG capture of the complete virtual desktop;
- rectangular region capture from one cached desktop frame;
- one overlay spanning the complete virtual desktop;
- multi-monitor and negative-coordinate support;
- separate configurable folders for full captures and snips;
- optional clipboard copy and capture sound;
- persistent JSON settings;
- per-stage capture timings and optional JSON Lines diagnostics;
- repeatable full-capture baseline benchmark;
- dedicated STA capture worker;
- one active capture plus one coalesced pending request;
- graceful shutdown that cancels an active overlay.

## Current hotkeys

| Shortcut | Action |
| --- | --- |
| `Ctrl + Shift + G` | Queue capture of the entire virtual desktop |
| `Ctrl + Shift + S` | Open the rectangular snipping overlay |
| `Ctrl + Shift + Q` | Exit SCapturer after active file work finishes |

Hotkeys use `MOD_NOREPEAT`, so holding a combination does not continuously enqueue captures.

## Rectangular snipping

Region capture follows this pipeline:

1. capture the complete virtual desktop once;
2. create one dimmed cached frame;
3. display a topmost overlay across all monitors;
4. update only selection rendering while the mouse moves;
5. crop the selected rectangle from the original cached frame;
6. save the crop as lossless PNG;
7. optionally publish it to the clipboard.

The desktop is not captured again while the selection rectangle moves. The saved image therefore never contains the dimming layer, border, or size label.

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
- P4 — high-performance rectangular snipping overlay: complete in this milestone;
- P5 — mixed-DPI and multi-monitor hardening: next.

Part of **X-LAB** — practical automation utilities.
