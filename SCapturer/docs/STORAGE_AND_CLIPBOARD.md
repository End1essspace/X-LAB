# Storage and Clipboard Behavior

SCapturer treats durable PNG persistence as the capture's success boundary. Clipboard publication is optional and occurs only after the image has been committed under its final file name.

A clipboard failure therefore produces a warning, not a failed screenshot.

## Default locations

| Capture type | Default folder |
| --- | --- |
| Full desktop | `%USERPROFILE%\Pictures\SCapturer\Full` |
| Region | `%USERPROFILE%\Pictures\SCapturer\Snips` |

The folders can be changed from **Save Locations**. The selected paths are stored in `%LOCALAPPDATA%\SCapturer\config.json`.

## Atomic PNG transaction

Full and region captures use the same persistence sequence:

1. normalize and validate the destination folder;
2. create the folder when it does not exist;
3. verify write access;
4. allocate a unique temporary path in that folder;
5. encode the complete image to `*.scapturer.tmp`;
6. verify that the temporary file exists and is not empty;
7. flush the file to the storage device;
8. rename it to a collision-safe final `.png` name;
9. publish the final path to recent captures and diagnostics.

The temporary file and final file remain in the same directory and volume. The final name is not visible until encoding and the explicit flush have completed.

When any pre-commit step fails, SCapturer attempts to delete the temporary file and reports the capture as failed.

## File naming and collisions

Base names include a millisecond timestamp:

```text
Screenshot_2026-07-20_01-42-18-315.png
Snip_2026-07-20_01-42-18-315.png
```

If another file already owns the candidate name, SCapturer appends a numeric suffix:

```text
Screenshot_2026-07-20_01-42-18-315_1.png
```

The existing file is never overwritten, and the PNG is not re-encoded while searching for a free name.

## Folder validation and fallback

The configured destination is validated before the capture buffer is allocated. Validation includes path normalization, directory creation, a write probe, and a conservative path-length check.

SCapturer uses a 240-character persistence boundary to avoid ambiguous behavior on Windows installations where long-path support is not consistently available.

The matching default folder is used as a fallback when the configured path is:

- empty or malformed;
- inaccessible or read-only;
- unavailable at capture time;
- longer than the conservative boundary;
- unable to pass the write probe.

Fallback does not rewrite the user's configured setting. The capture result records the actual path and adds a `StorageFallback` warning, which is also available to the console and diagnostics log.

If both the configured folder and the default folder are unavailable, the capture fails.

## Free-space protection

Before PNG encoding on a local drive, SCapturer requires at least:

```text
raw frame size + 8 MB reserve
```

The raw size is calculated from frame stride and height. This is deliberately conservative because the final PNG size cannot be known before encoding.

UNC paths and provider-backed paths that cannot be represented reliably through `DriveInfo` skip the preflight check. For those destinations, the encoder and filesystem error remain the source of truth.

## Temporary-file cleanup

The first use of each destination folder in a process removes abandoned `*.scapturer.tmp` files older than 24 hours.

The cleanup is best-effort:

- recent temporary files are retained;
- locked files are retained;
- cleanup failures do not block a new capture.

The write probe and cleanup result are cached per folder for the lifetime of the process.

## Clipboard publication

Clipboard work runs on one dedicated STA thread named:

```text
SCapturer Clipboard Dispatcher
```

The dispatcher has a bounded queue of one request. SCapturer clones the image before enqueueing it, allowing the capture frame to be released independently of clipboard ownership.

When Windows reports that the clipboard is locked, publication retries with exponential delays:

```text
25 ms → 50 ms → 100 ms → 200 ms → 400 ms
```

Retries stop after a two-second publication window. The capture caller waits for at most three seconds for the dispatcher result.

The queue remains bounded: a second clipboard request is rejected rather than growing an unbounded backlog.

## Failure semantics

| Condition | PNG result | Pipeline result | Additional behavior |
| --- | --- | --- | --- |
| Encoder, flush, or rename fails | No final file is exposed | `FAILED` | Temporary cleanup is attempted |
| Configured folder fails, fallback succeeds | Valid PNG in default folder | Completed | `StorageFallback` warning |
| Clipboard is busy or times out | Valid PNG remains committed | Completed | `ClipboardPublication` warning |
| Both configured and fallback folders fail | No final file | `FAILED` | Error identifies both destinations |

Recent captures only expose committed final files. Clipboard publication cannot delete or invalidate an already committed PNG.

## Diagnostics

When diagnostics are enabled, each completed capture can record:

- final file path and size;
- capture kind and dimensions;
- backend name;
- storage fallback warnings;
- clipboard publication warnings;
- persistence and clipboard timing.

The metrics log is stored at:

```text
%LOCALAPPDATA%\SCapturer\diagnostics\capture-metrics.jsonl
```

## Verification expectations

Storage and clipboard behavior is considered healthy when:

- interrupted encoding never exposes a partial final PNG;
- name collisions preserve existing files;
- fallback saves produce a valid PNG and a structured warning;
- local free-space failures occur before encoding;
- clipboard contention does not fail or delete the PNG;
- retry and caller wait times remain bounded;
- repeated captures do not accumulate recent temporary files;
- full and region capture behave consistently across both backends.
