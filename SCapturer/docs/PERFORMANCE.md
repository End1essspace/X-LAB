# SCapturer Performance Baseline and P9 Persistence Hardening

## Backend architecture

P7 keeps two complete implementations behind `ICaptureBackend`.

### Reference GDI+

- managed `Bitmap` frame;
- `Graphics.CopyFromScreen`;
- GDI+ crop and PNG encoder.

### Native GDI + WIC

- top-down 32-bit `CreateDIBSection`;
- `BitBlt` into a reusable memory device context;
- direct native crop with `BitBlt`;
- WIC PNG encoder receiving the BGRA buffer through `WritePixels`.

The native frame exposes a temporary `Bitmap` view over the same DIB memory for the existing overlay and clipboard boundaries. It does not copy the complete frame into a second managed bitmap.

## Opaque alpha normalization

A 32-bit GDI desktop DIB does not provide a reliable alpha byte. The native backend normalizes every captured pixel to opaque BGRA before WIC encoding or overlay use.

This avoids transparent PNG output while preserving RGB values.

The normalization pass is included in pixel-acquisition timing.

## Existing measurements

Both backends report the same metrics:

- dispatch;
- directory preparation;
- buffer allocation;
- physical pixel acquisition;
- PNG persistence;
- clipboard publication;
- sound dispatch;
- total duration;
- managed allocations on the capture thread;
- working set before and after capture.

Each result also records the backend kind and display name.

## Selected-backend baseline

The existing baseline benchmark uses the currently selected backend. Its report schema is version `2.0` and includes the backend mode and actual backend name.

This supports repeated measurements after the comparison decision.

## Backend comparison

The comparison benchmark runs one warm-up and ten measured captures for each backend under the same settings and target volume.

The decision uses:

```text
native p95 improvement = (reference p95 - native p95) / reference p95
native allocation improvement = (reference allocations - native allocations) / reference allocations
```

Native passes only when:

- median total duration does not regress by more than 5%; and
- p95 total duration or managed allocations improve by at least 20%.

The recommended mode is persisted automatically after a successful comparison.

## Resource ownership

A native frame owns:

- one memory device context;
- one selected DIB section;
- one direct pixel pointer;
- one non-owning managed `Bitmap` view.

Disposal order is fixed:

1. dispose the managed view;
2. restore the previous selected GDI object;
3. delete the DIB section;
4. delete the memory device context.

WIC encoder, frame encoder, property bag, and output stream COM references are released after every persistence operation.

## Bounded pipeline

P7 does not change the queue limit:

```text
one active request + one coalesced pending request
```

No backend may start a parallel PNG encoder or create another overlay worker.

## P7 acceptance checks

P7 is accepted when:

- both backends build and create valid opaque PNG files;
- full and region capture work through both backends;
- WIC unavailability falls back visibly to reference;
- the comparison report contains two complete sample sets;
- the persisted recommendation matches the documented gate;
- native does not regress mixed-DPI or topology handling;
- repeated backend switching does not leak resources;
- console responsiveness remains equivalent to P6.


## P9 persistence cost

`PngPersistenceMilliseconds` now includes complete encoding, validation, explicit disk flush, and atomic same-directory rename. P9 baseline numbers are therefore expected to include a small durability cost compared with P7.

The operation does not expose a final file until all of those steps succeed. Benchmark temporary PNG files use the same transaction, keeping production and benchmark persistence comparable.

## Clipboard isolation

Clipboard publication runs on a dedicated STA dispatcher and is measured in `ClipboardMilliseconds`. The dispatcher is bounded to one request and retries a locked clipboard for at most two seconds with exponential backoff.

A clipboard failure adds a warning but does not change the successful PNG transaction into a failed capture. Benchmarks continue to disable clipboard publication.

## Storage checks

Destination folder validation is cached per process. Stale temporary cleanup runs once per folder, not on every capture. Local free-space checks are O(1) and occur before PNG encoding.

## P9 acceptance checks

P9 is accepted when:

- persistence timings remain bounded after adding flush and rename;
- no final partial PNG appears after a forced encoder or disk failure;
- clipboard lock tests preserve the final PNG;
- fallback saves include a structured warning;
- repeated captures do not accumulate temporary files;
- the clipboard dispatcher and capture worker remain single-instance and bounded.
