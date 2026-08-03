# Console UI

The SCapturer console is the primary management interface for capture settings, diagnostics, recent files, and background behavior.

It is designed as a keyboard-first developer interface: compact, state-oriented, and responsive while capture work continues on background threads.

## Pages

### Dashboard

The Dashboard provides the main runtime overview:

- listener, console, pipeline, and benchmark state;
- active and requested capture backend;
- monitor count, DPI mode, session type, desktop bounds, and topology version;
- structured details for the most recent capture;
- full-desktop and region-capture commands;
- access to every management page;
- up to three recent session events.

### Capture Settings

Provides:

- clipboard-copy toggle;
- capture-sound toggle;
- requested backend selection;
- effective backend and fallback state;
- the fixed lossless PNG policy.

Changing the backend affects future requests only. Active and already-pending requests retain their settings snapshot.

### Hotkeys

Provides bindings for:

- full-desktop capture;
- region capture;
- console visibility;
- graceful exit;
- restoring defaults.

Bindings are entered as text and must contain at least one modifier and one primary key.

Examples:

```text
Ctrl+Shift+G
Ctrl+Alt+PrintScreen
Win+Shift+S
Alt+F10
```

Hotkey updates are transactional:

1. parse and normalize the requested chord;
2. reject duplicates inside the SCapturer binding set;
3. unregister the current set on the hotkey STA thread;
4. register the complete candidate set with Windows;
5. persist settings only after registration succeeds;
6. restore the previous set if registration fails.

SCapturer never leaves a partially applied hotkey set active.

### Save Locations

Provides:

- the current full-capture and region-capture folders;
- commands to change either folder;
- commands to open either folder in Explorer.

Long paths use middle truncation so the beginning and final directory or file name remain visible.

### Diagnostics

Provides:

- diagnostics logging toggle;
- selected-backend baseline benchmark;
- Reference GDI+ versus Native GDI + WIC comparison;
- access to the diagnostics folder;
- the benchmark decision gate.

During a benchmark, normal capture requests are rejected to prevent unrelated work from contaminating the measurements.

### Recent Captures

Displays up to twelve recent PNG files discovered from the configured full and region folders.

| Key | Action |
| --- | --- |
| `Enter` | Open the selected image |
| `F` | Open its containing folder |
| `R` | Rescan both capture folders |

The list is derived from the filesystem and does not require a database.

### Background and Startup

Displays:

- current console visibility;
- interactive or background launch state;
- console hotkey;
- Windows autostart state;
- the expected autostart command;
- stale registration or access errors;
- the native close-button handoff behavior.

The page can enable, disable, or repair current-user autostart and can hide the console while leaving SCapturer active.

### About

Shows the current version, runtime, capture and storage boundaries, and project identity. The page identifies **XCON** as the author under **X-LAB**.

## Navigation

Global controls:

| Key | Action |
| --- | --- |
| `↑` / `↓` | Move selection |
| `J` / `K` | Alternative selection controls |
| `Home` / `End` | Jump to the first or last item |
| `Enter` | Run the selected action |
| `1`–`9`, `0` | Run a visible item directly |
| `Esc` / `Backspace` | Return to the Dashboard |

Selection is retained independently for each page. The native console title follows the active page, for example `SCapturer — Diagnostics`.

The footer shows only controls relevant to the current page.

## Layout model

The interface uses a consistent structure:

1. centered page title;
2. runtime telemetry grid;
3. page-specific content;
4. status line;
5. context-aware navigation hints.

Runtime fields use fixed columns so changing values do not move unrelated labels across the screen. Menu shortcuts and state values are aligned to the right where space allows.

The minimum useful console size is `64×28`. Below that size, SCapturer displays a resize instruction instead of a partially clipped page.

## Semantic colors

Color is attached to explicit UI fields rather than inferred from arbitrary words.

| Color | Meaning |
| --- | --- |
| Cyan | Page title, native backend, running or background state, selection marker |
| Green | Healthy or enabled state, successful status token |
| Yellow | Warning, fallback, stale registration |
| Red | Error or failed state |
| Dark cyan | Section headings, requested backend, hotkey chords, X-LAB identity |
| Dark gray | Labels, rules, paths, help text, inactive state |
| White on dark gray | Selected menu row |

Status lines use one colored severity token—`INFO`, `OK`, `WARN`, or `ERROR`—while the message itself remains neutral.

This keeps color meaningful and avoids accidental emphasis inside ordinary sentences or menu labels.

## Differential rendering

The renderer builds a complete styled frame but writes only lines whose text or style changed.

Each render pass:

1. reads the current console dimensions;
2. builds and truncates the visible styled lines;
3. compares every line with the previous frame signature;
4. repositions the cursor only for changed lines;
5. overwrites lines that disappeared;
6. preserves page and selection state.

A full clear is used only when necessary:

- entering a text prompt;
- changing pages;
- resizing the terminal;
- recovering from unsupported cursor operations;
- showing a console whose previous frame is no longer valid.

Frequent pipeline or status updates therefore do not flash the complete window.

## Threading boundary

Only the controller thread interacts with console input and output:

- `Console.KeyAvailable`;
- `Console.ReadKey`;
- differential rendering;
- blocking text prompts.

Capture, hotkey, display-topology, IPC, and benchmark workers publish immutable state updates and request a redraw. They never write directly to the terminal.

When the console is hidden, rendering and key polling stop. The controller continues processing hotkeys, IPC commands, capture events, and shutdown requests at a reduced management-loop cadence.

## Status and session events

`AppController` keeps a bounded in-memory queue of session events. It records useful runtime changes such as:

- listener startup;
- capture completion, cancellation, or failure;
- clipboard and storage warnings;
- display-topology changes;
- IPC activation;
- benchmark state.

The Dashboard shows up to three newest entries. This event feed is intentionally temporary; persistent capture diagnostics remain in the diagnostics files.

## Capture warnings

A capture remains successful when its PNG has been committed but an optional post-processing step fails.

Examples:

- the configured folder was unavailable and the default folder was used;
- Windows kept the clipboard locked through the retry window.

The Dashboard shows the warning count, the status line shows the current message, and diagnostics store structured warnings when enabled.

## Text prompts

Hotkey and folder edits use a dedicated prompt view showing:

- prompt title;
- current value;
- input guidance;
- an empty-input cancellation path.

After input completes, the previous page is invalidated and redrawn from current state.

## Hidden-console behavior

Normal hide/show actions keep the same process, service graph, and console attachment. Hiding only changes window visibility.

Closing the native console with `X` follows the separate background handoff path described in [Background and autostart](BACKGROUND_AND_AUTOSTART.md). When the replacement instance later shows the console, the renderer starts with a clean frame.
