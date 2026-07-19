# SCapturer Background Lifecycle and Autostart

## Runtime modes

SCapturer remains one process with the same capture, hotkey, display, persistence, and clipboard services whether its console is visible or hidden.

Interactive launch:

```powershell
dotnet run --project .\src\SCapturer.App\SCapturer.App.csproj -c Release
```

Background launch:

```powershell
dotnet run --project .\src\SCapturer.App\SCapturer.App.csproj -c Release -- --background
```

SCapturer is built as `WinExe`. Background launch therefore creates no console window. Interactive launch and later show requests allocate a console, bind standard input/output, and redraw the management UI without restarting capture services. Hiding frees only the console attachment; the process remains alive.

## Console visibility

The default console hotkey is:

```text
Ctrl + Shift + H
```

The hotkey is configurable and participates in the same all-or-nothing `RegisterHotKey` transaction as full capture, region capture, and exit.

Hiding the console does not stop:

- global hotkeys;
- full or region capture;
- display-topology observation;
- clipboard publication;
- diagnostics;
- single-instance IPC.

When hidden, the console management loop reduces its wake-up cadence from 40 ms to 200 ms. Capture and hotkey threads remain event-driven.

## Single-instance activation

SCapturer uses a local named mutex to permit one primary process per Windows session.

A second invocation sends one command to the primary instance through a current-user named pipe, then exits.

Default second launch behavior:

```text
show the existing SCapturer console
```

Supported command-line actions:

| Argument | Existing instance | New primary instance |
| --- | --- | --- |
| no argument | show console | start interactive |
| `--background` | no-op | start hidden |
| `--show` | show console | start interactive |
| `--hide` | hide console | start hidden |
| `--toggle-console` | toggle console | start, then toggle |
| `--capture-full` | queue full capture | start hidden and queue full capture |
| `--capture-region` | queue region capture | start hidden and open region selection |
| `--cancel-region` | cancel an active region selection | start hidden; no-op if no selection is active |
| `--exit` | request graceful exit | exit without starting services |

The IPC server does not execute capture work. It places commands into the application command queue, which is processed by the main controller loop.

## Windows autostart

Autostart is registered for the current Windows user under:

```text
HKCU\Software\Microsoft\Windows\CurrentVersion\Run
```

Value name:

```text
SCapturer
```

The command always includes:

```text
--background
```

No administrator privileges, scheduled task, service, or Startup-folder shortcut is required.

The Background and Startup page distinguishes:

- `DISABLED` — no registration;
- `ENABLED` — registration matches the current executable path;
- `STALE` — a registration exists but points to another build or location;
- `ERROR` — Windows registry state could not be read.

Selecting repair overwrites only the `SCapturer` value with the current executable command.

## Shutdown

All exit sources use the existing graceful path:

- console menu;
- exit hotkey;
- `--exit` sent by another invocation.

SCapturer stops accepting new captures, cancels an active selection overlay, finishes active persistence work, stops benchmark work, unregisters hotkeys, closes the IPC server, and exits.

## Reliability isolation

The P11 harness uses a unique internal instance suffix and data directory so it can exercise single-instance activation without touching the normal SCapturer process. It also disables global hotkey registration for the isolated process; lifecycle and capture requests continue through the production named-pipe command path.
