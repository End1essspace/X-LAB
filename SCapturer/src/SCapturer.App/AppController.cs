using System.Diagnostics;
using SCapturer.App.UI;
using SCapturer.Core.Benchmarking;
using SCapturer.Core.Diagnostics;
using SCapturer.Core.Display;
using SCapturer.Core.Models;
using SCapturer.Core.Pipeline;
using SCapturer.Core.Services;

namespace SCapturer.App;

internal sealed class AppController
{
    private readonly SettingsStore _settingsStore;
    private readonly CaptureCoordinator _captureCoordinator;
    private readonly CaptureDiagnosticsStore _diagnosticsStore;
    private readonly BaselineBenchmarkService _benchmarkService;
    private readonly DisplayTopologyService _displayTopology;
    private readonly ConsoleUi _consoleUi;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _uiStateGate = new();

    private AppSettings _settings;
    private CaptureResult? _lastCapture;
    private CapturePipelineSnapshot _pipelineSnapshot = CapturePipelineSnapshot.Initial;
    private Task? _benchmarkTask;
    private string _statusMessage = "Ready.";
    private int _benchmarkInProgress;
    private int _renderRequested = 1;

    public AppController(
        SettingsStore settingsStore,
        CaptureCoordinator captureCoordinator,
        CaptureDiagnosticsStore diagnosticsStore,
        BaselineBenchmarkService benchmarkService,
        DisplayTopologyService displayTopology,
        ConsoleUi consoleUi)
    {
        _settingsStore = settingsStore;
        _captureCoordinator = captureCoordinator;
        _diagnosticsStore = diagnosticsStore;
        _benchmarkService = benchmarkService;
        _displayTopology = displayTopology;
        _consoleUi = consoleUi;
        _settings = _settingsStore.Load();

        _captureCoordinator.StateChanged += OnPipelineStateChanged;
        _captureCoordinator.CaptureCompleted += OnCaptureCompleted;
        _captureCoordinator.CaptureCancelled += OnCaptureCancelled;
        _captureCoordinator.CaptureFailed += OnCaptureFailed;
        _displayTopology.TopologyChanged += OnDisplayTopologyChanged;
    }

    public int Run()
    {
        _captureCoordinator.Start();

        using var hotkeys = new HotkeyService();
        hotkeys.FullCaptureRequested += CaptureFullFromHotkey;
        hotkeys.RegionCaptureRequested += CaptureRegionFromHotkey;
        hotkeys.ExitRequested += RequestExitFromHotkey;
        hotkeys.DisplayConfigurationChanged += OnHotkeyDisplayConfigurationChanged;
        hotkeys.Start();

        SetStatus(
            "Listener active. Ctrl+Shift+G captures the desktop; " +
            "Ctrl+Shift+S opens region selection; Ctrl+Shift+Q exits.");

        try
        {
            while (!_shutdown.IsCancellationRequested)
            {
                if (Interlocked.Exchange(ref _renderRequested, 0) == 1)
                {
                    RenderMainMenu();
                }

                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(intercept: true);
                    HandleMenuKey(key);
                }

                Thread.Sleep(40);
            }
        }
        finally
        {
            _displayTopology.TopologyChanged -= OnDisplayTopologyChanged;
            _shutdown.Cancel();
            _captureCoordinator.Stop(TimeSpan.FromSeconds(15));
            WaitForBenchmarkShutdown();
        }

        return 0;
    }

    private void HandleMenuKey(ConsoleKeyInfo key)
    {
        switch (char.ToUpperInvariant(key.KeyChar))
        {
            case '1':
                QueueCapture(
                    CaptureKind.FullDesktop,
                    Stopwatch.GetTimestamp(),
                    "ConsoleFull");
                break;
            case '2':
                QueueCapture(
                    CaptureKind.Region,
                    Stopwatch.GetTimestamp(),
                    "ConsoleSnip");
                break;
            case '3':
                ChangeCaptureFolder(
                    CaptureKind.FullDesktop,
                    "CHANGE FULL CAPTURE FOLDER");
                break;
            case '4':
                ChangeCaptureFolder(
                    CaptureKind.Region,
                    "CHANGE SNIP CAPTURE FOLDER");
                break;
            case '5':
                OpenCaptureFolder(CaptureKind.FullDesktop);
                break;
            case '6':
                OpenCaptureFolder(CaptureKind.Region);
                break;
            case '7':
                SaveSettings(ToggleClipboardCopy());
                break;
            case '8':
                SaveSettings(ToggleCaptureSound());
                break;
            case '9':
                SaveSettings(ToggleDiagnostics());
                break;
            case 'B':
                StartBaselineBenchmark();
                break;
            case '0':
                _shutdown.Cancel();
                break;
            default:
                SetStatus($"Unknown option: {key.KeyChar}");
                break;
        }
    }

    private void CaptureFullFromHotkey(long requestTimestamp)
    {
        QueueCapture(CaptureKind.FullDesktop, requestTimestamp, "HotkeyFull");
    }

    private void CaptureRegionFromHotkey(long requestTimestamp)
    {
        QueueCapture(CaptureKind.Region, requestTimestamp, "HotkeySnip");
    }

    private string ToggleClipboardCopy()
    {
        lock (_uiStateGate)
        {
            _settings.CopyToClipboard = !_settings.CopyToClipboard;
            return $"Clipboard copy {EnabledText(_settings.CopyToClipboard)}.";
        }
    }

    private string ToggleCaptureSound()
    {
        lock (_uiStateGate)
        {
            _settings.PlayCaptureSound = !_settings.PlayCaptureSound;
            return $"Capture sound {EnabledText(_settings.PlayCaptureSound)}.";
        }
    }

    private string ToggleDiagnostics()
    {
        lock (_uiStateGate)
        {
            _settings.EnableDiagnostics = !_settings.EnableDiagnostics;
            return $"Capture diagnostics {EnabledText(_settings.EnableDiagnostics)}.";
        }
    }

    private void QueueCapture(
        CaptureKind kind,
        long requestTimestamp,
        string trigger)
    {
        if (Volatile.Read(ref _benchmarkInProgress) == 1)
        {
            SetStatus("The baseline benchmark is running; capture request ignored.");
            return;
        }

        var result = _captureCoordinator.TryEnqueue(
            kind,
            GetSettingsSnapshot(),
            requestTimestamp,
            trigger);

        var captureName = kind == CaptureKind.Region
            ? "Region capture"
            : "Full capture";

        SetStatus(result switch
        {
            CaptureEnqueueResult.Accepted => $"{captureName} queued.",
            CaptureEnqueueResult.Coalesced =>
                $"{captureName} replaced the single pending request with the latest request.",
            CaptureEnqueueResult.Stopping => "Capture pipeline is stopping.",
            _ => "Capture request was not accepted.",
        });
    }

    private void StartBaselineBenchmark()
    {
        if (Interlocked.CompareExchange(ref _benchmarkInProgress, 1, 0) != 0)
        {
            SetStatus("The baseline benchmark is already running.");
            return;
        }

        if (_captureCoordinator.HasWork)
        {
            Volatile.Write(ref _benchmarkInProgress, 0);
            SetStatus("Wait for the active capture queue to become idle before benchmarking.");
            return;
        }

        var settingsSnapshot = GetSettingsSnapshot();
        SetStatus("Starting baseline benchmark: 1 warm-up + 10 measured full captures.");

        _benchmarkTask = Task.Run(() =>
        {
            try
            {
                var result = _benchmarkService.Run(
                    settingsSnapshot,
                    measuredIterations: 10,
                    progress: progress =>
                    {
                        SetStatus(
                            $"{progress.Phase}: {progress.CurrentIteration}/{progress.TotalIterations}.");
                    },
                    cancellationToken: _shutdown.Token);

                SetStatus(
                    $"Benchmark complete. Median {result.Summary.MedianTotalMilliseconds:0.0} ms; " +
                    $"p95 {result.Summary.P95TotalMilliseconds:0.0} ms. " +
                    $"Report: {result.ReportFilePath}");
            }
            catch (OperationCanceledException)
            {
                SetStatus("Benchmark cancelled during shutdown.");
            }
            catch (Exception exception)
            {
                SetStatus($"Benchmark failed: {exception.Message}");
            }
            finally
            {
                Volatile.Write(ref _benchmarkInProgress, 0);
                RequestRender();
            }
        });
    }

    private void OnPipelineStateChanged(CapturePipelineSnapshot snapshot)
    {
        lock (_uiStateGate)
        {
            if (snapshot.Version >= _pipelineSnapshot.Version)
            {
                _pipelineSnapshot = snapshot;
            }
        }

        RequestRender();
    }

    private void OnCaptureCompleted(CaptureCompletedEvent completed)
    {
        var diagnosticsWarning = string.Empty;

        if (completed.Settings.EnableDiagnostics)
        {
            try
            {
                _diagnosticsStore.Record(completed.Result);
            }
            catch (Exception exception)
            {
                diagnosticsWarning = $" Diagnostics log failed: {exception.Message}";
            }
        }

        var captureName = completed.Kind == CaptureKind.Region
            ? "region"
            : "full desktop";

        lock (_uiStateGate)
        {
            _lastCapture = completed.Result;
            _statusMessage =
                $"Saved {captureName} {completed.Result.Width}×{completed.Result.Height} PNG " +
                $"({FormatBytes(completed.Result.FileSizeBytes)}) in " +
                $"{completed.Result.Metrics.TotalMilliseconds:0.0} ms: " +
                $"{completed.Result.FilePath}.{diagnosticsWarning}";
        }

        RequestRender();
    }

    private void OnCaptureCancelled(CaptureCancelledEvent cancelled)
    {
        var message = cancelled.Reason switch
        {
            CaptureCancellationReason.DisplayTopologyChanged =>
                "Region capture cancelled because the display configuration changed.",
            CaptureCancellationReason.Shutdown =>
                "Region capture cancelled during shutdown.",
            _ when cancelled.Kind == CaptureKind.Region =>
                "Region capture cancelled.",
            _ => "Capture cancelled.",
        };

        SetStatus(message);
    }

    private void OnDisplayTopologyChanged(DisplayTopologyChange change)
    {
        RequestRender();
    }

    private void OnHotkeyDisplayConfigurationChanged()
    {
        _displayTopology.NotifyExternalChange(
            "The hotkey message window received WM_DISPLAYCHANGE");
    }

    private void OnCaptureFailed(CaptureFailedEvent failed)
    {
        SetStatus(
            $"Capture failed ({failed.Kind}, {failed.Trigger}): " +
            failed.Exception.Message);
    }

    private void ChangeCaptureFolder(
        CaptureKind kind,
        string title)
    {
        var settings = GetSettingsSnapshot();
        var currentFolder = kind == CaptureKind.Region
            ? settings.SnipCaptureFolder
            : settings.FullCaptureFolder;

        var enteredPath = _consoleUi.PromptForFolder(title, currentFolder);
        if (string.IsNullOrWhiteSpace(enteredPath))
        {
            SetStatus("Folder change cancelled.");
            return;
        }

        try
        {
            var expanded = Environment.ExpandEnvironmentVariables(
                enteredPath.Trim().Trim('"'));
            var fullPath = Path.GetFullPath(expanded);
            Directory.CreateDirectory(fullPath);

            lock (_uiStateGate)
            {
                if (kind == CaptureKind.Region)
                {
                    _settings.SnipCaptureFolder = fullPath;
                }
                else
                {
                    _settings.FullCaptureFolder = fullPath;
                }
            }

            SaveSettings(
                $"{(kind == CaptureKind.Region ? "Snip" : "Full capture")} " +
                $"folder changed to: {fullPath}");
        }
        catch (Exception exception)
        {
            SetStatus($"Invalid folder: {exception.Message}");
        }
    }

    private void OpenCaptureFolder(CaptureKind kind)
    {
        try
        {
            var settings = GetSettingsSnapshot();
            var captureFolder = kind == CaptureKind.Region
                ? settings.SnipCaptureFolder
                : settings.FullCaptureFolder;

            Directory.CreateDirectory(captureFolder);
            Process.Start(new ProcessStartInfo
            {
                FileName = captureFolder,
                UseShellExecute = true,
            });

            SetStatus(
                kind == CaptureKind.Region
                    ? "Snip folder opened."
                    : "Full capture folder opened.");
        }
        catch (Exception exception)
        {
            SetStatus($"Could not open folder: {exception.Message}");
        }
    }

    private void RequestExitFromHotkey()
    {
        SetStatus("Exit hotkey received. Finishing active file operations.");
        _shutdown.Cancel();
    }

    private void SaveSettings(string successMessage)
    {
        try
        {
            _settingsStore.Save(GetSettingsSnapshot());
            SetStatus(successMessage);
        }
        catch (Exception exception)
        {
            SetStatus($"Could not save settings: {exception.Message}");
        }
    }

    private void RenderMainMenu()
    {
        AppSettings settings;
        string status;
        CaptureResult? lastCapture;
        CapturePipelineSnapshot pipeline;

        lock (_uiStateGate)
        {
            settings = _settings.CreateSnapshot();
            status = _statusMessage;
            lastCapture = _lastCapture;
            pipeline = _pipelineSnapshot;
        }

        _consoleUi.RenderMainMenu(
            settings,
            status,
            lastCapture,
            pipeline,
            _displayTopology.GetSnapshot(),
            Volatile.Read(ref _benchmarkInProgress) == 1);
    }

    private AppSettings GetSettingsSnapshot()
    {
        lock (_uiStateGate)
        {
            return _settings.CreateSnapshot();
        }
    }

    private void SetStatus(string message)
    {
        lock (_uiStateGate)
        {
            _statusMessage = message;
        }

        RequestRender();
    }

    private void RequestRender()
    {
        Interlocked.Exchange(ref _renderRequested, 1);
    }

    private void WaitForBenchmarkShutdown()
    {
        var benchmark = _benchmarkTask;
        if (benchmark is null)
        {
            return;
        }

        try
        {
            benchmark.Wait(TimeSpan.FromSeconds(15));
        }
        catch (AggregateException exception)
            when (exception.InnerExceptions.All(inner => inner is OperationCanceledException))
        {
            // Cancellation during shutdown is expected.
        }
    }

    private static string EnabledText(bool value) => value ? "enabled" : "disabled";

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double value = bytes;
        var unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.##} {units[unitIndex]}";
    }
}
