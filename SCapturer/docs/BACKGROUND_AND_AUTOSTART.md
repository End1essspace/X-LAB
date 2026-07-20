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

SCapturer is built as `WinExe`. Background launch therefore creates no console window. Interactive launch and later show requests allocate a console, bind standard input/output, and redraw the management UI without restarting capture services. After the first allocation, ordinary hide/show operations reuse the console attachment and change only window visibility; the process remains alive.

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

## Native close-button behavior

Windows treats the `X` button of a native console differently from an application hide command. It emits `CTRL_CLOSE_EVENT`, and Windows terminates the attached process after the registered handlers return. That termination cannot be cancelled by returning `TRUE`.

SCapturer handles this through a controlled background handoff:

1. the close handler starts the same executable with an internal `--resume-background=<pid>` argument;
2. the replacement waits for the closing process to release its PID and single-instance mutex;
3. it starts the normal listener hidden with the same persisted settings;
4. launching the EXE or pressing the console hotkey shows the management console again.

This path is used only for the native close button. **Hide console**, `--hide`, and the console hotkey continue hiding the existing process without restart. An active capture should be allowed to finish before pressing `X`, because Windows controls the close-event termination deadline.

## Installer maintenance commands

The packaged MSI uses two internal executable arguments:

```text
--shutdown-for-update
--prepare-uninstall
```

Both request graceful shutdown through the existing single-instance IPC path and wait up to 45 seconds for the primary mutex to be released. `--prepare-uninstall` additionally removes the current-user SCapturer autostart value. These arguments are reserved for installer maintenance and are not shown in the normal command list.
