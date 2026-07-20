# SCapturer release packaging

SCapturer produces two Windows x64 release artifacts from one verified publish output:

- a portable ZIP;
- a per-user MSI installer.

Both packages contain the same self-contained single-file `SCapturer.exe`.

## Release identity

Default first packaged release:

```text
Product        SCapturer
Publisher      X-LAB
Author         XCON
Version        0.1.0
Architecture   win-x64
Target         .NET 8 self-contained
```

An application icon is intentionally not configured yet. Adding one later does not require changing the packaging architecture.

## Prerequisites

- Windows 10 version 2004 or newer;
- .NET 8 SDK;
- WiX Toolset 3.14 for MSI generation.

The produced EXE is self-contained; target computers do not need a separately installed .NET runtime.

## One-command release build

Run from the repository root:

```powershell
.\scripts\build-release.ps1 -Version 0.1.0
```

The script performs:

1. clean;
2. restore;
3. Release build;
4. deterministic logic tests;
5. full reliability gate;
6. self-contained single-file publish;
7. portable ZIP creation;
8. MSI compilation and linking;
9. SHA-256 generation;
10. artifact validation.

Optional development switches:

```powershell
.\scripts\build-release.ps1 -Version 0.1.0 -SkipReliability
```

```powershell
.\scripts\build-release.ps1 -Version 0.1.0 -SkipMsi
```

These switches are intended for local iteration. A release candidate should be built without skips.

## Output

```text
dist\release\0.1.0\
├─ SCapturer-v0.1.0-win-x64-portable.zip
├─ SCapturer-v0.1.0-win-x64.msi
├─ RELEASE_NOTES.md
└─ SHA256SUMS.txt
```

The temporary publish and WiX object directories are deleted after a successful build.

## Publish profile

The deterministic publish profile is stored at:

```text
src\SCapturer.App\Properties\PublishProfiles\win-x64.pubxml
```

It explicitly enables:

- `win-x64`;
- self-contained deployment;
- single-file output;
- native-library extraction from the single executable;
- no trimming;
- no ReadyToRun;
- no debug symbols.

The .NET SDK may emit optional portable PDB files for referenced projects even when the application publish profile disables symbols. The release script removes any `*.pdb` files from the publish staging directory, then rejects the output unless exactly one non-empty deployable file named `SCapturer.exe` remains.

## Portable package

The portable archive contains files at ZIP root:

```text
SCapturer.exe
README.txt
```

It does not alter the registry during extraction. SCapturer autostart remains an explicit runtime setting controlled from the application.

For stable autostart behavior, keep the portable executable in a stable folder. If it is moved, SCapturer reports the existing autostart registration as stale and can repair it.

## MSI package

The WiX source is:

```text
packaging\windows\SCapturer.wxs
```

Installation scope is per-user. No administrator elevation is required.

Installation directory:

```text
%LOCALAPPDATA%\Programs\X-LAB\SCapturer\SCapturer.exe
```

Start Menu shortcut:

```text
X-LAB\SCapturer
```

No desktop shortcut is created.

The application component uses an HKCU installer marker as its Windows Installer key path. This is required for a component that installs into the current user's profile and owns a non-advertised Start Menu shortcut. Empty per-user installation and Start Menu directories are explicitly removed during uninstall.

WiX `ICE91` is suppressed deliberately during linking because the package is permanently scoped to `perUser`; the warning only describes the hypothetical behavior of the same directory tree in a per-machine package. All other ICE validation remains enabled.

## Autostart ownership

The MSI does not enable Windows autostart. The user enables or disables it from SCapturer.

On a real uninstall, the MSI invokes:

```text
SCapturer.exe --prepare-uninstall
```

This command:

1. removes the SCapturer current-user Run value;
2. asks a running SCapturer instance to exit gracefully;
3. waits for the single-instance mutex to be released;
4. returns a failure code if shutdown does not complete within 45 seconds.

During repair or major upgrade, the MSI instead invokes:

```text
SCapturer.exe --shutdown-for-update
```

This preserves the autostart preference while still waiting for graceful process shutdown before file replacement.

Both arguments are internal maintenance commands and are not part of the normal interactive command surface.

## Upgrade policy

The MSI uses a stable `UpgradeCode` and a generated `ProductCode` for each build. Versions follow:

```text
major.minor.patch
```

Examples:

```text
0.1.0
0.1.1
0.2.0
1.0.0
```

Installing a newer MSI performs a major upgrade. Installing an older version over a newer version is blocked.

## Uninstall data policy

MSI uninstall removes:

- installed `SCapturer.exe`;
- Start Menu shortcut;
- empty installation directories;
- SCapturer autostart registration.

MSI uninstall preserves:

```text
%USERPROFILE%\Pictures\SCapturer\
%LOCALAPPDATA%\SCapturer\config.json
%LOCALAPPDATA%\SCapturer\diagnostics\
```

Screenshots and application data are user-owned and must not be silently deleted by the installer.

## Manual publish

To produce only the self-contained executable:

```powershell
dotnet publish .\src\SCapturer.App\SCapturer.App.csproj -p:PublishProfile=win-x64 -p:Version=0.1.0 -p:AssemblyVersion=0.1.0.0 -p:FileVersion=0.1.0.0 -p:InformationalVersion=0.1.0 -o .\dist\publish\win-x64
```

## Packaging acceptance

Use [`RELEASE_CHECKLIST.md`](RELEASE_CHECKLIST.md) before publishing artifacts externally.
