# SCapturer Display Validation Matrix

Run all checks from a Release build:

```powershell
dotnet run --project .\src\SCapturer.App\SCapturer.App.csproj -c Release
```

For each successful region capture, compare the visible selection edge with the saved PNG at 100% zoom.

## A. Single monitor

1. 1920×1080 at 100% scaling.
2. 2560×1440 at 125% scaling.
3. 3840×2160 at 150% scaling.

Expected:

- console virtual bounds equal the configured physical resolution;
- full PNG dimensions equal the displayed virtual bounds;
- region edges are pixel-aligned;
- size label equals the saved PNG dimensions.

## B. Horizontal multi-monitor

1. Primary monitor on the left.
2. Primary monitor on the right.
3. Secondary monitor physically left of primary.
4. Different resolutions on each monitor.
5. Mixed scaling: 100% and 150%.

Expected:

- negative X is shown when a monitor is left of primary;
- overlay covers every monitor exactly once;
- dragging across the monitor boundary produces one continuous region;
- saved region coordinates and dimensions match the selection.

## C. Vertical and offset layouts

1. Secondary monitor above primary.
2. Secondary monitor below primary.
3. Monitors with staggered top edges.

Expected:

- negative Y is preserved for monitors above primary;
- no transparent or duplicated strips appear in the overlay;
- selection clamps only at the physical virtual-desktop edges.

## D. Live topology changes

While the overlay is open:

1. change one monitor resolution;
2. change scaling;
3. disconnect a secondary monitor;
4. reconnect the monitor;
5. change the primary display.

Expected:

- active region selection closes automatically;
- status explains that display topology changed;
- no snip PNG is created;
- the next overlay uses the new bounds;
- the console topology version increases.

## E. Full capture during change

Trigger full capture while changing resolution or connecting a display.

Expected:

- SCapturer either completes from one stable topology or retries once;
- no partial or dimensionally corrupt PNG is written;
- a repeated unstable change reports a capture failure instead of looping.

## F. Power and session transitions

1. Put Windows to sleep and resume.
2. Lock and unlock the session.
3. Connect through Remote Desktop.
4. Disconnect Remote Desktop and return to console.

Expected:

- topology information refreshes;
- local/remote state updates;
- hotkeys continue working;
- no restart is required;
- an overlay active during transition is cancelled.

## G. Resource check

Repeat 20 topology changes and 50 region cancellations.

Observe the process in Task Manager or Process Explorer.

Expected:

- working set returns near its post-warmup range;
- GDI and USER handles do not grow linearly;
- only one capture worker exists;
- no orphan overlay window remains.
