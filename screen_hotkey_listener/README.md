**Screen Hotkey Listener**

A single-file Windows utility that registers global hotkeys
for full-screen capture directly to the clipboard.

No GUI.
No external dependencies.
Runs silently in the background.

**Features**

* Global hotkeys (system-wide)
* Multi-monitor screenshot support
* Clipboard image injection
* Autostart installation (Startup folder)
* WinAPI-based hotkey registration
* Fallback key polling if `WM_HOTKEY` is blocked
* Runtime C# compilation via PowerShell


**Hotkeys**

| Shortcut             | Action                            |
| -------------------- | --------------------------------- |
| **Ctrl + Shift + G** | Capture all monitors to clipboard |
| **Ctrl + Shift + Q** | Quit listener                     |


**How It Works**

1. On first run, the script installs itself into the Windows Startup folder.
2. It extracts an embedded PowerShell payload.
3. PowerShell compiles a small C# listener at runtime.
4. A hidden window registers global hotkeys via `RegisterHotKey`.
5. If hotkey messages are blocked, a polling fallback detects physical key states.

All logic is contained in a single `.bat` file.


**Installation**

Simply run:

```
screen_hotkey_listener.bat
```

On first execution:

* A shortcut is created in the Startup folder.
* The listener starts automatically.

No administrator privileges required.

**Technical Notes**

* Uses `System.Windows.Forms` and `System.Drawing`
* Screenshot is captured via `Graphics.CopyFromScreen`
* Entire virtual desktop is captured (`SystemInformation.VirtualScreen`)
* Clipboard updated using `Clipboard.SetImage`
* Designed for Windows environments


**Platform**

Windows


**Removal**

To remove:

1. Delete the shortcut from:

```
%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup
```

2. Stop the running listener.


Part of **X-LAB** — practical automation utilities.
