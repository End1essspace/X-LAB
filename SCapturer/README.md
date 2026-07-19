# SCapturer

SCapturer is a performance-first Windows screenshot utility with global hotkeys, lossless PNG persistence, an interactive console management interface, built-in diagnostics, and a bounded asynchronous capture pipeline.

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
- repeatable baseline benchmark with median and p95 reporting;
- dedicated STA capture worker;
- non-blocking hotkey and console dispatch;
- one active capture plus one coalesced pending request;
- graceful capture-pipeline shutdown.

## Current hotkeys

| Shortcut | Action |
| --- | --- |
| `Ctrl + Shift + G` | Queue capture of the entire virtual desktop |
| `Ctrl + Shift + Q` | Exit SCapturer after active file work finishes |

## Bounded capture pipeline

Normal screenshots no longer execute on the hotkey message-loop thread or the console thread.

SCapturer uses:

- one dedicated STA capture worker;
- one active capture;
- one pending request slot;
- a bounded channel used only as the worker wake-up signal.

When more requests arrive while both slots are occupied, the pending request is replaced by the newest request. This prevents unbounded tasks, duplicate PNG encoders, and uncontrolled memory growth while preserving the user's latest intent.

Pipeline states are visible in the console:

```text
IDLE
QUEUED
CAPTURING
SAVING
PUBLISHING
FINALIZING
COMPLETED
FAILED
STOPPING
```

## Performance instrumentation

Each capture records:

- dispatch latency;
- capture-directory preparation;
- bitmap allocation;
- physical pixel acquisition;
- PNG encoding and persistence;
- clipboard publication;
- sound dispatch;
- total capture duration;
- managed allocations on the capture worker;
- working-set values before and after capture.

Diagnostics can be enabled from console option `6`. Entries are appended to:

```text
%LOCALAPPDATA%\SCapturer\diagnostics\capture-metrics.jsonl
```

Console option `7` runs one warm-up capture followed by ten measured captures. The benchmark now runs outside the console loop and can be cancelled during shutdown between samples. User captures are rejected while the benchmark owns the baseline capture path.

Reports are saved under:

```text
%LOCALAPPDATA%\SCapturer\diagnostics\benchmarks
```

See [`docs/PERFORMANCE.md`](docs/PERFORMANCE.md) for measurement semantics and limitations.

## Repository structure

```text
SCapturer.sln
Directory.Build.props
src/
  SCapturer.App/       Console executable and application lifecycle
  SCapturer.Core/
    Benchmarking/
    Diagnostics/
    Models/
    Pipeline/          Bounded coordinator and pipeline state models
    Services/
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
- P2 — performance metrics and baseline benchmark harness: complete;
- P3 — bounded asynchronous capture pipeline: complete in this milestone;
- P4 — high-performance rectangular snipping overlay: next.

Part of **X-LAB** — practical automation utilities.
