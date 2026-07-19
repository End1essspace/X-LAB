# SCapturer

SCapturer is a performance-first Windows screenshot utility with global hotkeys, lossless PNG persistence, and an interactive console management interface.

The project began as a single-file Batch/PowerShell experiment. The active implementation is now a standalone C# application split into an executable shell and a reusable core library. The original prototype remains under `legacy/` for historical reference only.

## Current capabilities

- interactive console UI;
- single-instance process guard;
- global Windows hotkeys through `RegisterHotKey`;
- lossless PNG capture of the complete virtual desktop;
- multi-monitor and negative-coordinate support;
- configurable capture folder;
- optional clipboard copy and capture sound;
- persistent JSON settings;
- automatic migration of the previous X-LAB configuration.

## Current hotkeys

| Shortcut | Action |
| --- | --- |
| `Ctrl + Shift + G` | Capture the entire virtual desktop |
| `Ctrl + Shift + Q` | Exit SCapturer |

The hotkeys use Windows virtual-key codes and therefore refer to the same physical keys under different keyboard layouts.

## Repository structure

```text
SCapturer.sln
Directory.Build.props
src/
  SCapturer.App/     Console executable and application lifecycle
  SCapturer.Core/    Capture, hotkey, settings, and persistence services
docs/
legacy/
```

The release executable is named `SCapturer.exe`.

## Default storage

New installations save full captures to:

```text
%USERPROFILE%\Pictures\SCapturer\Full
```

Settings are stored in:

```text
%LOCALAPPDATA%\SCapturer\config.json
```

When the new settings file does not exist, SCapturer imports valid settings from the previous location without deleting the original file:

```text
%LOCALAPPDATA%\X-LAB\ScreenCaptureTool\config.json
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

Output:

```text
dist\SCapturer\SCapturer.exe
```

## Roadmap status

- P0 — standalone C# foundation: complete;
- P1 — SCapturer identity and solution normalization: complete in this milestone;
- P2 — performance metrics and baseline benchmark harness: next;
- P3 — bounded asynchronous capture pipeline;
- P4 — high-performance rectangular snipping overlay.

See [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) for the current project boundaries.

Part of **X-LAB** — practical automation utilities.
