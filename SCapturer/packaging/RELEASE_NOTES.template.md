# SCapturer v{{VERSION}}

Released: {{DATE}}

Reliability patch for global hotkey conflicts and in-session recovery.

## Fixed

- SCapturer no longer terminates when a configured global hotkey is already registered by another process.
- Failed startup registration remains all-or-nothing; no partial hotkey set is left active.
- The **Hotkeys** page now captures a replacement shortcut directly from the keyboard.
- Multiple occupied bindings can be repaired one at a time: each valid replacement is saved while the global set remains inactive, and all four hotkeys activate automatically after the final conflict is removed.
- Reconfiguration of an already-active set remains transactional and rolls back if Windows rejects a candidate binding.
- Added automated coverage for single-conflict and staged multi-conflict recovery.

Capture behavior, storage, backends, and packaging layout are unchanged from v0.1.0.

## Downloads

- `SCapturer-v{{VERSION}}-win-x64-portable.zip`
- `SCapturer-v{{VERSION}}-win-x64.msi`

`SHA256SUMS.txt` contains checksums for both distributable files.

## Upgrade

The MSI performs the existing per-user major-upgrade flow and preserves screenshots, settings, diagnostics, and the current autostart preference.
