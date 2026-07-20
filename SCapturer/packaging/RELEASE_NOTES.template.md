# SCapturer v{{VERSION}}

Released: {{DATE}}

SCapturer is a performance-first Windows screenshot developer utility by **XCON**, built under **X-LAB**.

## Highlights

- full physical virtual-desktop capture;
- rectangular cached-frame region capture;
- Reference GDI+ and Native GDI + WIC backends;
- atomic PNG persistence and hardened clipboard publication;
- configurable global hotkeys;
- background operation and single-instance activation;
- developer console telemetry, diagnostics, benchmarks, and reliability gates;
- portable win-x64 package and per-user MSI installer.

## Installation

Use either:

- `SCapturer-v{{VERSION}}-win-x64-portable.zip` for a portable deployment;
- `SCapturer-v{{VERSION}}-win-x64.msi` for a per-user installation.

The MSI installs to `%LOCALAPPDATA%\Programs\X-LAB\SCapturer` and creates a Start Menu shortcut. Windows autostart remains opt-in from inside SCapturer.

## Upgrade and uninstall

The MSI requests graceful shutdown before repair, upgrade, or uninstall. Uninstall removes the application, Start Menu shortcut, and SCapturer autostart registration. It preserves screenshots, settings, and diagnostics.
