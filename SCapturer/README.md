# SCapturer

SCapturer is a performance-first Windows screenshot utility with lossless full-desktop capture, a cached-frame rectangular snipping overlay, global hotkeys, diagnostics, and an interactive console management interface.

The active implementation is a standalone C# application split into an executable shell and a reusable Windows capture core. The original Batch/PowerShell prototype remains under `legacy/` for historical reference only.

## Current capabilities

- interactive page-based console UI;
- arrow-key, Enter, Escape, and numeric navigation;
- differential console rendering without repeated `Console.Clear`;
- configurable global hotkeys with conflict validation and rollback;
- lossless PNG capture of the complete physical virtual desktop;
- rectangular region capture from one cached desktop frame;
- native monitor enumeration through `EnumDisplayMonitors` and `GetMonitorInfo`;
- Per-Monitor V2 DPI awareness;
- mixed-DPI and negative-coordinate support;
- automatic display-topology invalidation;
- safe monitor, sleep/resume, and remote-session transitions;
- separate configurable folders for full captures and region captures;
- recent-capture browser with file and folder opening;
- optional clipboard copy and capture sound;
- persistent JSON settings;
- per-stage timings and optional JSON Lines diagnostics;
- repeatable full-capture baseline benchmark;
- one dedicated STA capture worker;
- one active capture plus one coalesced pending request;
- graceful shutdown that cancels an active overlay.

## Default hotkeys

| Shortcut | Action |
| --- | --- |
| `Ctrl + Shift + G` | Queue capture of the complete virtual desktop |
| `Ctrl + Shift + S` | Open the rectangular snipping overlay |
| `Ctrl + Shift + Q` | Exit SCapturer after active file work finishes |

Hotkeys are editable from the **Hotkeys** page. SCapturer validates syntax and duplicate bindings, asks Windows to reserve the new combinations, and restores the previous registrations if an update fails.

Supported input examples:

```text
Ctrl+Shift+G
Ctrl+Alt+PrintScreen
Win+Shift+S
Alt+F10
```

## Console management UI

The dashboard provides access to:

1. full-desktop capture;
2. region capture;
3. capture settings;
4. hotkey settings;
5. save locations;
6. diagnostics and benchmark;
7. recent captures;
8. product information;
9. exit.

Navigation:

- `↑` / `↓` or `J` / `K` — change selection;
- `Enter` — activate;
- `1`–`9` — activate a visible menu item;
- `Esc` or `Backspace` — return to the dashboard;
- `R` on Recent Captures — refresh;
- `F` on Recent Captures — open the selected file's folder.

The renderer compares the next frame with the previous frame and rewrites only changed console lines. `Console.Clear` is reserved for page transitions, text prompts, terminal resize recovery, and fallback rendering.

See [`docs/CONSOLE_UI.md`](docs/CONSOLE_UI.md) for the UI contract.

## Display and DPI model

SCapturer enables `HighDpiMode.PerMonitorV2` before creating Windows Forms handles.

`DisplayTopologyService` enumerates visible monitors with native Win32 APIs and creates one physical-pixel virtual-desktop rectangle. Negative coordinates are retained for monitors positioned left of or above the primary display.

If display topology changes during a full capture, the in-memory frame is discarded and capture retries once. If display topology changes during region selection, the cached frame is invalidated and selection is cancelled without creating a PNG.

See [`docs/DISPLAY_TEST_MATRIX.md`](docs/DISPLAY_TEST_MATRIX.md).

## Rectangular snipping

Region capture:

1. acquires one stable physical display-topology snapshot;
2. captures the complete virtual desktop once;
3. creates one dimmed cached frame;
4. displays a topmost overlay over the exact physical desktop;
5. updates only dirty selection regions while the mouse moves;
6. crops from the original cached frame;
7. saves lossless PNG;
8. optionally publishes the result to the clipboard.

Controls:

- left mouse drag — select;
- `Esc` — cancel;
- right mouse button — cancel.

## Bounded capture pipeline

Full and region requests share one dedicated STA worker:

```text
one active capture + one coalesced pending capture
```

Repeated requests replace only the pending request. SCapturer does not create parallel overlays or parallel PNG encoders.

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
    UI/
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
dotnet run --project .\src\SCapturer.App\SCapturer.App.csproj -c Release
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
- P5 — mixed-DPI and multi-monitor hardening: complete;
- P6 — interactive console UI v1: complete in this milestone;
- P7 — native GDI and WIC capture pipeline: next.

Part of **X-LAB** — practical automation utilities.
