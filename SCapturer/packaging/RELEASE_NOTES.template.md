# SCapturer v{{VERSION}}

Released: {{DATE}}

Small reliability patch for global hotkey startup conflicts.

## Fixed

- SCapturer no longer terminates when a configured global hotkey is already registered by another process.
- Failed startup registration remains all-or-nothing; no partial hotkey set is left active.
- The hotkey message window stays available after a conflict, so a free binding can be applied from the **Hotkeys** page without restarting SCapturer.
- Added automated coverage for startup conflict recovery.

Capture behavior, storage, backends, and packaging layout are unchanged from v0.1.0.

## Downloads

- `SCapturer-v{{VERSION}}-win-x64-portable.zip`
- `SCapturer-v{{VERSION}}-win-x64.msi`

`SHA256SUMS.txt` contains checksums for both distributable files.

## Upgrade

The MSI performs the existing per-user major-upgrade flow and preserves screenshots, settings, diagnostics, and the current autostart preference.
