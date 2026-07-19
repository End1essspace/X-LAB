# SCapturer Performance Baseline and P6 Console UI

## Stable capture baseline

The current full-capture backend remains based on:

- `System.Drawing.Bitmap`;
- `Graphics.CopyFromScreen`;
- the built-in PNG encoder.

The P2 benchmark report schema remains compatible through P6.

## Capture measurements

Full capture records:

- dispatch;
- directory preparation;
- bitmap allocation;
- pixel acquisition;
- PNG persistence;
- clipboard publication;
- sound dispatch;
- total duration;
- managed allocations on the capture worker;
- working set before and after capture.

Region capture additionally records:

- overlay preparation;
- user interaction;
- crop duration.

## Bounded worker

Normal captures use one STA worker with:

```text
one active request + one coalesced pending request
```

The hotkey message thread and console thread do not perform capture or persistence work.

## Display consistency

Monitor topology is event-driven and cached. Capture uses a bounded stable-snapshot wait.

Full capture retries once if topology changes during pixel acquisition. Region capture cancels if its cached desktop frame becomes stale.

## P6 console rendering

P6 does not change the capture backend.

The console renderer:

- has no animation timer;
- does not render from background threads;
- compares complete logical frames;
- rewrites only changed terminal lines;
- performs a full clear only for page changes, prompts, resize, or recovery.

The existing 40 ms console input loop remains the only UI polling loop. No additional idle polling was introduced.

## Hotkey reconfiguration cost

Hotkey parsing and validation are user-triggered operations.

Registration changes execute on the hotkey STA thread and do not allocate capture buffers. A failed candidate set is rolled back before control returns to the UI.

No hotkey reconfiguration work occurs during idle operation.

## Recent capture cost

Recent-capture directory scanning occurs only:

- during application startup;
- when entering or refreshing the Recent Captures page;
- after capture-folder changes.

Successful captures are inserted directly into the in-memory list. The capture worker does not enumerate directories after every screenshot.

The list is bounded to twelve UI entries.

## P6 acceptance checks

P6 is accepted when:

- capture and benchmark timings remain within normal P5 variance;
- pipeline status updates do not flash the complete console;
- arrow navigation remains responsive during PNG persistence;
- terminal resize produces one clean redraw;
- invalid and duplicate hotkeys are rejected;
- unavailable Windows hotkeys leave the previous set active;
- successful hotkey changes work immediately and survive restart;
- recent capture opening does not block the capture worker;
- idle CPU does not materially increase from P5.

## P7 comparison rule

P7 must run the same baseline benchmark before and after native backend adoption.

The native backend is accepted only when it improves p95 latency or allocations without regressing:

- physical pixel correctness;
- mixed-DPI behavior;
- overlay cancellation;
- clipboard publication;
- console responsiveness;
- resource stability.
