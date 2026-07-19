# SCapturer Storage and Clipboard Contract

## Purpose

P9 separates durable PNG persistence from optional clipboard publication.

A screenshot is successful when its PNG has been committed under the final file name. Clipboard publication may succeed or fail independently and is represented as a warning rather than a failed capture.

## Atomic PNG transaction

For every full or region capture:

1. resolve and validate the destination folder;
2. allocate a unique temporary path in that same folder;
3. encode the complete PNG to `*.scapturer.tmp`;
4. verify that the temporary file exists and is non-empty;
5. flush the temporary file to the storage device;
6. rename it to a collision-safe final `.png` name;
7. expose the final path to recent captures and diagnostics.

The rename stays within one directory and volume. A partially encoded image is never published under its final name.

If any step before the rename fails, SCapturer deletes the temporary file on a best-effort basis and reports capture failure.

## Collision handling

The initial final name uses a millisecond timestamp:

```text
Screenshot_2026-07-20_01-42-18-315.png
Snip_2026-07-20_01-42-18-315.png
```

If another writer already owns that name, SCapturer increments a numeric suffix without re-encoding the PNG.

## Folder validation and fallback

The configured folder is normalized, created, and tested for write access before capture buffers are allocated.

SCapturer falls back to the corresponding default folder when the configured path is:

- empty or malformed;
- too long for the conservative Windows persistence boundary;
- inaccessible;
- read-only;
- unavailable at capture time.

Fallback locations:

```text
%USERPROFILE%\Pictures\SCapturer\Full
%USERPROFILE%\Pictures\SCapturer\Snips
```

The configured setting is not silently overwritten. The actual saved path and a `StorageFallback` warning are attached to the capture result and diagnostics entry.

## Free-space protection

Before encoding, local-drive free space is checked against:

```text
raw frame bytes + 8 MB reserve
```

This is deliberately conservative. Network and provider paths that cannot be represented through `DriveInfo` rely on the encoder and filesystem error as the source of truth.

## Temporary-file cleanup

The first use of a destination folder in each process removes abandoned `*.scapturer.tmp` files older than 24 hours.

Recent temporary files and locked files are left untouched.

## Clipboard dispatcher

Clipboard publication runs on one dedicated STA thread:

```text
SCapturer Clipboard Dispatcher
```

The dispatcher owns a bounded queue of one request. The image is cloned before transfer so the capture frame can be released independently after the publication request completes or times out.

When Windows reports a locked clipboard, the dispatcher retries with exponential delays:

```text
25 ms → 50 ms → 100 ms → 200 ms → 400 ms
```

Retries stop after a two-second publication window.

## Failure semantics

### PNG failure

- capture result is failed;
- no final `.png` is exposed;
- temporary-file cleanup is attempted;
- the pipeline reports `FAILED`.

### Clipboard failure

- final PNG remains valid;
- capture result is completed;
- a `ClipboardPublication` warning is attached;
- console status and diagnostics explain the failure.

## Acceptance checks

P9 is accepted when:

- an encoder interruption leaves no final partial PNG;
- final names do not overwrite existing files;
- a read-only configured folder produces a valid fallback PNG and warning;
- insufficient local free space fails before encoding;
- a locked clipboard does not fail or delete the PNG;
- clipboard retry completes within the bounded timeout;
- repeated captures leave no recent temporary files;
- diagnostics records storage and clipboard warnings;
- full and region capture remain compatible with both capture backends.
