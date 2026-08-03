# Background Operation and Autostart

SCapturer can keep its capture services active without leaving the management console visible. Console visibility affects only the user interface; it does not create a second capture runtime or stop the existing one.

## Runtime modes

Interactive launch:

```powershell
dotnet run --project .\src\SCapturer.App\SCapturer.App.csproj -c Release
```

Background launch:

```powershell
dotnet run --project .\src\SCapturer.App\SCapturer.App.csproj -c Release -- --background
```

SCapturer is built as a Windows executable (`WinExe`). A background launch starts without a console window. An interactive launch, or a later show request, allocates the console when needed, binds its standard streams, and renders the management interface.

After the first allocation, normal show and hide operations reuse the same console attachment and change only the window's visibility. The active process and its services remain unchanged.

## What remains active when the console is hidden

Hiding the console does not stop:

- global hotkeys;
- full-desktop and region capture;
- the bounded capture worker;
- clipboard publication;
- display-topology observation;
- diagnostics and benchmark state;
- single-instance command routing.

The default visibility hotkey is:

```text
Ctrl + Shift + H
```

It can be changed from the **Hotkeys** page and is registered in the same all-or-nothing transaction as the capture and exit hotkeys.

The management loop checks for input every 40 ms while the console is visible and every 200 ms while it is hidden. Capture, hotkey, display, clipboard, and IPC work remains event-driven.

## Closing the native console window

The `X` button on a native Windows console is not equivalent to an application hide command. Windows sends `CTRL_CLOSE_EVENT` and terminates the attached process after registered handlers return; returning `TRUE` does not cancel that termination.

SCapturer preserves background operation through a controlled handoff:

1. the close handler starts the same executable with an internal `--resume-background=<pid>` argument;
2. the replacement waits for the closing process to exit;
3. it acquires the normal single-instance mutex;
4. it resumes the listener hidden with the persisted settings.

The process identifier changes during this handoff. Launching SCapturer again, or pressing the configured console hotkey, shows the replacement instance.

This path is used only for the native close button. **Hide console**, `--hide`, and the visibility hotkey hide the current process without restarting it.

> Allow an active capture or benchmark to finish before pressing `X`. Windows controls the close-event termination deadline, so the handoff cannot guarantee completion of work still owned by the closing process.

## Single-instance command routing

SCapturer permits one primary instance per Windows session through a local named mutex.

A later invocation connects to a current-user named pipe, sends one command, and exits. The pipe server does not execute capture work directly; commands are placed into the application queue and processed by the main controller.

| Argument | When an instance is already running | When no instance is running |
| --- | --- | --- |
| no argument | Show the existing console | Start interactively |
| `--background` | No action | Start hidden |
| `--show` | Show the console | Start interactively |
| `--hide` | Hide the console | Start hidden |
| `--toggle-console` | Toggle visibility | Start and apply the toggle |
| `--capture-full` | Queue a full-desktop capture | Start hidden and queue a capture |
| `--capture-region` | Queue region selection | Start hidden and open region selection |
| `--cancel-region` | Cancel active region selection | Start hidden; otherwise no action |
| `--exit` | Request graceful shutdown | Exit without starting services |

The mutex and pipe names include the Windows session identifier, so separate interactive sessions do not share one process instance.

## Windows autostart

Autostart is optional and configured for the current user under:

```text
HKCU\Software\Microsoft\Windows\CurrentVersion\Run
```

Value name:

```text
SCapturer
```

The registered command points to the current executable and always includes:

```text
--background
```

No administrator privileges, Windows service, scheduled task, or Startup-folder shortcut is required.

The **Background and Startup** page reports:

| State | Meaning |
| --- | --- |
| `DISABLED` | No SCapturer Run value exists |
| `ENABLED` | The value matches the current executable and background argument |
| `STALE` | A value exists but points to another build or location |
| `ERROR` | The registry state could not be read |

Repairing autostart replaces only the `SCapturer` value. Other Run entries are not modified.

Portable users should repair the registration after moving the executable. MSI installations use a stable per-user installation path, so the registration normally remains current across launches.

## Graceful shutdown

The menu command, exit hotkey, and `--exit` invocation all use the same shutdown path.

SCapturer:

1. stops accepting new capture requests;
2. discards the single pending request;
3. cancels an active region-selection overlay;
4. waits for active persistence work to stop within the configured shutdown window;
5. cancels benchmark work;
6. unregisters global hotkeys;
7. closes the named-pipe server;
8. releases owned runtime resources.

## Installer maintenance

The MSI package uses two internal arguments:

```text
--shutdown-for-update
--prepare-uninstall
```

Both request graceful shutdown through the normal single-instance channel and wait up to 45 seconds for the primary mutex to be released.

`--prepare-uninstall` also removes the current-user SCapturer autostart value. These commands are reserved for installer maintenance and are intentionally omitted from the normal command list.

## Reliability-test isolation

The reliability harness launches SCapturer with isolated mutex, pipe, and data-directory identifiers. It also disables global hotkey registration for the test process, preventing collisions with a normal background instance.

Lifecycle and capture commands still travel through the production named-pipe path, so the harness exercises the same activation and shutdown behavior used by the packaged application.
