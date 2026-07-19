# SCapturer

SCapturer is a performance-first Windows screenshot utility with global hotkeys, lossless PNG persistence, an interactive console management interface, and built-in capture diagnostics.

The active implementation is a standalone C# application split into an executable shell and a reusable core library. The original Batch/PowerShell prototype remains under `legacy/` for historical reference only.

## Current capabilities

- interactive console UI;
- single-instance process guard;
- global Windows hotkeys through `RegisterHotKey`;
- lossless PNG capture of the complete virtual desktop;
- multi-monitor and negative-coordinate support;
- configurable capture folder;
- optional clipboard copy and capture sound;
- persistent JSON settings;
- automatic migration of the previous X-LAB configuration;
- per-stage capture timings;
- optional JSON Lines diagnostics log;
- repeatable baseline benchmark with median and p95 reporting.

## Current hotkeys

| Shortcut | Action |
| --- | --- |
| `Ctrl + Shift + G` | Capture the entire virtual desktop |
| `Ctrl + Shift + Q` | Exit SCapturer |

## Performance instrumentation

Each capture records:

- hotkey or console dispatch latency;
- capture-directory preparation;
- bitmap allocation;
- physical pixel acquisition;
- PNG encoding and persistence;
- clipboard update;
- capture sound dispatch;
- total capture duration;
- managed allocations on the capture thread;
- working-set values before and after capture.

Diagnostics can be enabled from console option `6`. Entries are appended to:

```text
%LOCALAPPDATA%\SCapturer\diagnostics\capture-metrics.jsonl
```

Console option `7` runs one warm-up capture followed by ten measured captures. Clipboard and sound are disabled for benchmark samples, and temporary PNG files are removed after measurement. Reports are saved under:

```text
%LOCALAPPDATA%\SCapturer\diagnostics\benchmarks
```

See [`docs/PERFORMANCE.md`](docs/PERFORMANCE.md) for measurement semantics and limitations.

## Repository structure

```text
SCapturer.sln
Directory.Build.props
src/
  SCapturer.App/     Console executable and application lifecycle
  SCapturer.Core/    Capture, diagnostics, benchmark, hotkey, and settings services
docs/
legacy/
```

## Default storage

New installations save full captures to:

```text
%USERPROFILE%\Pictures\SCapturer\Full
```

Settings are stored in:

```text
%LOCALAPPDATA%\SCapturer\config.json
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
- P1 — SCapturer identity and solution normalization: complete;
- P2 — performance metrics and baseline benchmark harness: complete in this milestone;
- P3 — bounded asynchronous capture pipeline: next;
- P4 — high-performance rectangular snipping overlay.

Part of **X-LAB** — practical automation utilities.
