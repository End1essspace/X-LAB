# Reliability validation

P11 adds two complementary verification layers:

1. a deterministic automated test executable for logic that does not require a live desktop;
2. a Windows integration and resource-soak harness for the complete background application lifecycle.

Neither layer changes the production capture queue or introduces a test framework dependency.

## Automated tests

Project:

```text
tests/SCapturer.Tests
```

Run:

```powershell
dotnet run --project .\tests\SCapturer.Tests\SCapturer.Tests.csproj -c Release
```

The executable returns `0` only when every case passes. Coverage includes:

- hotkey parsing, formatting, validation, and duplicate rejection;
- settings snapshots, normalization, invalid-file backup, and isolated paths;
- command-line lifecycle parsing;
- benchmark median and p95 calculations;
- atomic PNG collision handling and temporary-file cleanup;
- failed encoder cleanup;
- recent-capture ordering and bounds;
- autostart command construction;
- capture-pipeline work-state semantics.

The test project uses an internal lightweight runner instead of external NuGet test packages. This keeps clean builds deterministic and permits the same command in CI and offline Windows environments.

## Resource-soak harness

Project:

```text
tools/SCapturer.Reliability
```

Build the complete solution first, then run:

```powershell
dotnet run --project .\tools\SCapturer.Reliability\SCapturer.Reliability.csproj -c Release -- --captures 100 --console-cycles 30 --region-cancel-cycles 5 --process-cycles 10
```

The default application path is the Release framework-dependent executable:

```text
src\SCapturer.App\bin\Release\net8.0-windows10.0.19041.0\SCapturer.exe
```

A published executable can be selected explicitly:

```powershell
dotnet run --project .\tools\SCapturer.Reliability\SCapturer.Reliability.csproj -c Release -- --app .\dist\SCapturer\SCapturer.exe --captures 1000 --console-cycles 100 --region-cancel-cycles 20 --process-cycles 25
```

## Isolation

The harness never reuses the normal SCapturer process identity or data directory. It launches the tested application with internal environment overrides:

```text
SCAPTURER_INSTANCE_SUFFIX
SCAPTURER_DATA_DIRECTORY
SCAPTURER_DISABLE_HOTKEYS=1
SCAPTURER_NONINTERACTIVE=1
```

This creates:

- a unique mutex and named pipe;
- an isolated settings and diagnostics root;
- isolated Full and Snips folders;
- no global-hotkey collision with the user's normal background instance.

Capture and lifecycle operations are driven through the same named-pipe command path used by a second executable invocation.

## Workload

The default run performs:

- 5 warm-up full captures;
- representative resource warm-up: two console show/hide cycles and one region-overlay cancellation;
- 100 measured full captures;
- 30 console show/hide cycles;
- 5 region-overlay cancellation cycles;
- 10 complete process launch/show/hide/exit cycles.

Full captures use the configured production persistence path. Region cancellation opens the real cached-frame overlay, then sends `--cancel-region`; no Snips PNG may be committed.

## Resource sampling

The harness samples the tested process after warm-up and throughout the run:

- GDI object count;
- USER object count;
- process handle count;
- thread count;
- private memory;
- working set.

The baseline is captured only after every measured subsystem has been initialized: full capture, IPC, console allocation/rebinding, WinForms overlay creation, and region cancellation. This prevents one-time Windows/.NET resource caches from being misclassified as leaks. The final sample is captured after a two-second settling period.

After the management console is allocated for the first time, SCapturer keeps that single attachment and implements later hide/show operations with window visibility only. The soak harness therefore validates repeated console lifecycle without repeatedly creating standard-input/output handles.

Default non-growth gates:

| Resource | Maximum final delta |
| --- | ---: |
| GDI objects | `+8` |
| USER objects | `+8` |
| Process handles | `+32` |
| Threads | `+3` |
| Private memory | greater of `48 MB` or `20%` of baseline |
| Working set | greater of `64 MB` or `30%` of baseline |

These limits are designed to detect linear accumulation while allowing runtime caches, console reattachment state, and normal Windows working-set variance.

## Output artifacts

Each run writes to:

```text
artifacts/reliability/<timestamp>/
```

Files:

```text
soak-summary.json
resource-samples.jsonl
reliability-report.md
app-data/
captures/
```

The harness returns:

- `0` — every reliability gate passed;
- `1` — the run completed but one or more gates failed;
- `2` — the harness could not start or the environment was invalid.

## Acceptance

P11 is accepted only when:

- the solution builds with zero warnings and errors;
- every automated test passes;
- all requested captures complete;
- region cancellations produce no final PNG;
- no `.scapturer.tmp` file remains;
- IPC commands complete without failure;
- every repeated primary process exits gracefully;
- all resource-delta gates pass;
- the generated report is retained as release evidence.
