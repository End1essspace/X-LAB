# X-LAB Screen Capture

A Windows screenshot utility being migrated from the original single-file Batch/PowerShell prototype to a standalone C# executable.

The first migration milestone provides:

- an interactive console UI;
- a single-instance process guard;
- global Windows hotkeys via `RegisterHotKey`;
- lossless PNG capture of the complete virtual desktop;
- multi-monitor and negative-coordinate support;
- automatic saving to a configurable folder;
- optional clipboard copy and capture sound;
- persistent JSON settings under `%LOCALAPPDATA%`.

The previous Batch implementation is retained under `legacy/` for reference only. It is no longer the target architecture.

## Current hotkeys

| Shortcut | Action |
| --- | --- |
| `Ctrl + Shift + G` | Capture the entire virtual desktop |
| `Ctrl + Shift + Q` | Exit the application |

The hotkeys are based on Windows virtual-key codes, so they use the same physical keys under English and Russian keyboard layouts.

## Default storage

Full captures are saved as lossless PNG files to:

```text
%USERPROFILE%\Pictures\X-LAB Screenshots\Full
```

The folder can be changed from the console UI.

Settings are stored in:

```text
%LOCALAPPDATA%\X-LAB\ScreenCaptureTool\config.json
```

## Requirements for development

- Windows 10 or Windows 11
- .NET 8 SDK

## Run from source

```powershell
dotnet run --project .\src\ScreenCaptureTool\ScreenCaptureTool.csproj
```

## Build

```powershell
dotnet build .\src\ScreenCaptureTool\ScreenCaptureTool.csproj -c Release
```

## Publish as a self-contained single EXE

```powershell
dotnet publish .\src\ScreenCaptureTool\ScreenCaptureTool.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false -o .\dist\ScreenCaptureTool
```

The executable will be written to:

```text
dist\ScreenCaptureTool\XLab.ScreenCaptureTool.exe
```

## Migration roadmap

1. Standalone C# executable, console UI, full-desktop PNG capture — current milestone.
2. Native region-selection overlay similar to Windows 11 Snipping Tool.
3. Separate folders and naming policies for full captures and region captures.
4. Editable hotkeys and richer console settings.
5. Background/autostart mode with console show/hide control.
6. Packaging, release artifacts, tests, and legacy Batch removal.

Part of **X-LAB** — practical automation utilities.
