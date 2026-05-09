🧪 **X-LAB**

**X-LAB** is the utilities and automation branch of the **XCON | RX** ecosystem.

It contains small, focused tools built to solve real workflow problems quickly: Windows helpers, automation scripts, batch utilities, and lightweight system tools.

Minimal. Direct. Practical.


📦 **Utilities**


📸 **screen_hotkey_listener**

A single-file Windows utility for capturing all monitors directly to the clipboard using global hotkeys.

**Highlights:**

- `Ctrl + Shift + G` → capture all monitors to clipboard
- `Ctrl + Shift + Q` → quit listener
- Multi-monitor screenshot support
- Clipboard image injection
- Optional autostart through the Windows Startup folder
- WinAPI-based hotkey registration with fallback key polling

**Folder:** [`screen_hotkey_listener/`](screen_hotkey_listener/)


⚡ **toggle_power**

A lightweight batch utility for switching between Windows power plans.

**Highlights:**

- Toggles between **Balanced** and **High Performance**
- Uses native Windows `powercfg`
- No GUI
- No external dependencies
- Useful before gaming, rendering, testing, or heavy workloads

**Folder:** [`toggle_power/`](toggle_power/)


🧪 **git_publish_pack**

A Windows BAT automation pack for publishing projects from a working folder to a clean GitHub folder.

```text
working folder → GitHub folder → commit → push → tag
```

**Highlights:**

* Connects a local folder to an existing GitHub repository
* Creates backup before sync
* Syncs working project files into a clean GitHub folder
* Shows `git status` before commit
* Helps with commit, push, and release tags
* Uses local `config.bat` for project-specific paths

**Folder:** [`git_publish_pack/`](git_publish_pack/)


🛡 **win_malware_case_collector**

A read-only Windows malware triage and security audit collector.

It collects useful evidence from a suspicious Windows system into a structured Desktop folder and ZIP archive.

**Highlights:**

* Quick and full collection modes
* Read-only by design
* Collects processes, services, scheduled tasks, startup entries, registry persistence, network state, DNS cache, Defender status, and event logs
* Includes hashes, digital signature checks, browser extension manifests, and execution traces
* Generates `SUMMARY_RED_FLAGS.txt`, `ERRORS_WARNINGS.txt`, and `COLLECTION_COVERAGE.txt`
* Does not remove malware, clean the system, change registry values, or upload anything

**Folder:** [`win_malware_case_collector/`](win_malware_case_collector/)


🖥 **Platform**

Primarily focused on **Windows**.

Common technologies used in this repository:

* Batch scripts
* PowerShell
* Native Windows commands
* Lightweight automation helpers
* Small OS-level utilities


🧭 **Ecosystem**

**XCON | RX** is structured into several branches:

| Branch       | Purpose                                            |
| ------------ | -------------------------------------------------- |
| **X-SERIES** | Production desktop systems                         |
| **R-SERIES** | Research and experimental engineering              |
| **X-LAB**    | Utilities, scripts, and practical automation tools |

X-LAB is where small tools live before they become larger systems — or remain small because that is exactly what they need to be.


📁 **Repository Structure**

```text
X-LAB/
│
├── screen_hotkey_listener/
│   └── README.md
│
├── toggle_power/
│   └── README.md
│
├── git_publish_pack/
│   ├── 00_connect_repo.bat
│   ├── 01_sync.bat
│   ├── 02_commit.bat
│   ├── 03_push.bat
│   ├── 04_tag.bat
│   ├── 99_full_publish.bat
│   ├── config.example.bat
│   ├── CHANGELOG.md
│   └── README.md
│
├── .gitignore
├── LICENSE
└── README.md
```


🚀 **Usage**

Each utility has its own folder and README.

Open the folder of the tool you need, read its local instructions, and run the script from there.

Example:

```text
git_publish_pack/
```

or:

```text
screen_hotkey_listener/
```

or:

```text
toggle_power/
```


🧾 **License**

MIT License unless stated otherwise inside a specific utility folder.


👨‍💻 **Author**

**XCON | RX**

* Telegram: [@End1essspace](https://t.me/End1essspace)
* GitHub: [End1essspace](https://github.com/End1essspace)
