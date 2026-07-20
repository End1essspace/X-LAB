# SCapturer release checklist

## Source gate

- [ ] repository is clean;
- [ ] version is `major.minor.patch`;
- [ ] Release build has zero warnings and zero errors;
- [ ] automated tests pass;
- [ ] full reliability gate passes;
- [ ] release notes describe the current version.

## Artifact gate

- [ ] release script completes without skip switches;
- [ ] portable ZIP exists and opens;
- [ ] portable ZIP contains only `SCapturer.exe` and `README.txt` at root;
- [ ] MSI exists and is non-empty;
- [ ] `SHA256SUMS.txt` matches both distributable files;
- [ ] EXE Product, Company, Author, FileVersion, and ProductVersion metadata are correct;
- [ ] no `.dll`, `.pdb`, `.deps.json`, or `.runtimeconfig.json` files are distributed beside the EXE;
- [ ] application icon omission is intentional for this release.

## Portable validation

- [ ] launch from a clean folder;
- [ ] full capture works;
- [ ] region capture works;
- [ ] clipboard and sound settings work;
- [ ] `X` closes the console and resumes the listener in background;
- [ ] launching the same EXE again shows the existing console;
- [ ] `Ctrl+Shift+H` hides and shows the console;
- [ ] autostart can be enabled and disabled;
- [ ] moving the EXE produces a stale-autostart state rather than silent failure;
- [ ] portable removal leaves screenshots and settings untouched.

## MSI clean-install validation

- [ ] install succeeds without administrator elevation;
- [ ] installed path is `%LOCALAPPDATA%\Programs\X-LAB\SCapturer`;
- [ ] Start Menu shortcut is `X-LAB\SCapturer`;
- [ ] no desktop shortcut is created;
- [ ] installed EXE opens the developer console;
- [ ] full and region captures work;
- [ ] background mode and second-instance activation work;
- [ ] autostart remains disabled until explicitly enabled in SCapturer;
- [ ] Programs and Features shows one SCapturer entry with publisher X-LAB.

## MSI upgrade validation

- [ ] install the previous MSI and start SCapturer in background;
- [ ] enable autostart;
- [ ] install the newer MSI;
- [ ] the running process exits gracefully;
- [ ] file replacement succeeds without reboot or locked-file prompt;
- [ ] settings remain unchanged;
- [ ] screenshots remain unchanged;
- [ ] autostart remains enabled and points to the stable installed EXE;
- [ ] only one Programs and Features entry remains;
- [ ] downgrade installation is blocked.

## MSI repair validation

- [ ] start SCapturer and keep it hidden;
- [ ] run MSI repair;
- [ ] SCapturer exits gracefully before repair;
- [ ] repair completes without a reboot request;
- [ ] application launches normally afterward;
- [ ] autostart preference is preserved.

## MSI uninstall validation

- [ ] start SCapturer in background;
- [ ] uninstall from Programs and Features;
- [ ] SCapturer exits gracefully;
- [ ] installed EXE is removed;
- [ ] Start Menu shortcut is removed;
- [ ] SCapturer Run-key value is removed;
- [ ] screenshots remain;
- [ ] `%LOCALAPPDATA%\SCapturer\config.json` remains;
- [ ] diagnostics remain;
- [ ] reinstall loads the preserved configuration.
