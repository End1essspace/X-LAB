using System.Collections.Concurrent;
using System.Diagnostics;
using SCapturer.App.Lifecycle;
using SCapturer.App.UI;
using SCapturer.Core.Benchmarking;
using SCapturer.Core.Capture;
using SCapturer.Core.Diagnostics;
using SCapturer.Core.Display;
using SCapturer.Core.Models;
using SCapturer.Core.Pipeline;
using SCapturer.Core.Services;

namespace SCapturer.App;

internal sealed class AppController
{
    private readonly AppPaths _paths;
    private readonly SettingsStore _settingsStore;
    private readonly AutostartService _autostartService;
    private readonly CaptureCoordinator _captureCoordinator;
    private readonly CaptureDiagnosticsStore _diagnosticsStore;
    private readonly BaselineBenchmarkService _benchmarkService;
    private readonly BackendComparisonBenchmarkService _comparisonBenchmarkService;
    private readonly CaptureBackendProvider _backendProvider;
    private readonly DisplayTopologyService _displayTopology;
    private readonly HotkeyService _hotkeyService;
    private readonly RecentCaptureService _recentCaptureService;
    private readonly ConsoleUi _consoleUi;
    private readonly ConsoleVisibilityService _consoleVisibility;
    private readonly AppInstanceService _instanceService;
    private readonly bool _startedInBackground;
    private readonly bool _globalHotkeysEnabled;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly ConcurrentQueue<AppInstanceCommand> _externalCommands = new();
    private readonly object _uiStateGate = new();

    private AppSettings _settings;
    private CaptureResult? _lastCapture;
    private CapturePipelineSnapshot _pipelineSnapshot = CapturePipelineSnapshot.Initial;
    private IReadOnlyList<RecentCaptureItem> _recentCaptures;
    private AutostartStatus _autostartStatus;
    private Task? _benchmarkTask;
    private string _statusMessage = "Ready.";
    private int _benchmarkInProgress;
    private int _renderRequested = 1;

    public AppController(
        AppPaths paths,
        SettingsStore settingsStore,
        AutostartService autostartService,
        CaptureCoordinator captureCoordinator,
        CaptureDiagnosticsStore diagnosticsStore,
        BaselineBenchmarkService benchmarkService,
        BackendComparisonBenchmarkService comparisonBenchmarkService,
        CaptureBackendProvider backendProvider,
        DisplayTopologyService displayTopology,
        HotkeyService hotkeyService,
        RecentCaptureService recentCaptureService,
        ConsoleUi consoleUi,
        ConsoleVisibilityService consoleVisibility,
        AppInstanceService instanceService,
        bool startedInBackground,
        bool globalHotkeysEnabled)
    {
        _paths = paths;
        _settingsStore = settingsStore;
        _autostartService = autostartService;
        _captureCoordinator = captureCoordinator;
        _diagnosticsStore = diagnosticsStore;
        _benchmarkService = benchmarkService;
        _comparisonBenchmarkService = comparisonBenchmarkService;
        _backendProvider = backendProvider;
        _displayTopology = displayTopology;
        _hotkeyService = hotkeyService;
        _recentCaptureService = recentCaptureService;
        _consoleUi = consoleUi;
        _consoleVisibility = consoleVisibility;
        _instanceService = instanceService;
        _startedInBackground = startedInBackground;
        _globalHotkeysEnabled = globalHotkeysEnabled;

        _settings = _settingsStore.Load();
        _recentCaptures = _recentCaptureService.Load(_settings, maximumCount: 12);
        _autostartStatus = _autostartService.GetStatus();

        _captureCoordinator.StateChanged += OnPipelineStateChanged;
        _captureCoordinator.CaptureCompleted += OnCaptureCompleted;
        _captureCoordinator.CaptureCancelled += OnCaptureCancelled;
        _captureCoordinator.CaptureFailed += OnCaptureFailed;
        _displayTopology.TopologyChanged += OnDisplayTopologyChanged;
        _instanceService.CommandReceived += OnInstanceCommandReceived;
    }

    internal void QueueExternalCommand(AppInstanceCommand command)
    {
        if (command == AppInstanceCommand.None)
        {
            return;
        }

        _externalCommands.Enqueue(command);
        RequestRender();
    }

    public int Run()
    {
        _captureCoordinator.Start();

        var initialSettings = GetSettingsSnapshot();

        if (_globalHotkeysEnabled)
        {
            _hotkeyService.FullCaptureRequested += CaptureFullFromHotkey;
            _hotkeyService.RegionCaptureRequested += CaptureRegionFromHotkey;
            _hotkeyService.ExitRequested += RequestExitFromHotkey;
            _hotkeyService.ToggleConsoleRequested += RequestToggleConsoleFromHotkey;
            _hotkeyService.DisplayConfigurationChanged += OnHotkeyDisplayConfigurationChanged;
            _hotkeyService.Start(HotkeyBindingService.CreateSet(initialSettings));

            SetStatus(
                $"Listener active. Full {HotkeyBindingService.Format(initialSettings.FullCaptureHotkey)}; " +
                $"region {HotkeyBindingService.Format(initialSettings.RegionCaptureHotkey)}; " +
                $"console {HotkeyBindingService.Format(initialSettings.ToggleConsoleHotkey)}; " +
                $"exit {HotkeyBindingService.Format(initialSettings.ExitHotkey)}.");
        }
        else
        {
            SetStatus(
                "Reliability isolation active. Global hotkeys are disabled; " +
                "capture and lifecycle commands remain available through IPC.");
        }

        try
        {
            while (!_shutdown.IsCancellationRequested)
            {
                ProcessExternalCommands();
                var consoleVisible = _consoleVisibility.IsVisible;

                if (consoleVisible && _consoleUi.HasWindowSizeChanged())
                {
                    RequestRender();
                }

                if (consoleVisible &&
                    Interlocked.Exchange(ref _renderRequested, 0) == 1)
                {
                    _consoleUi.Render(CreateViewModel());
                }

                if (consoleVisible && Console.KeyAvailable)
                {
                    var key = Console.ReadKey(intercept: true);
                    var command = _consoleUi.HandleKey(key, CreateViewModel());
                    ExecuteCommand(command);
                }

                Thread.Sleep(consoleVisible ? 40 : 200);
            }
        }
        finally
        {
            _displayTopology.TopologyChanged -= OnDisplayTopologyChanged;
            if (_globalHotkeysEnabled)
            {
                _hotkeyService.FullCaptureRequested -= CaptureFullFromHotkey;
                _hotkeyService.RegionCaptureRequested -= CaptureRegionFromHotkey;
                _hotkeyService.ExitRequested -= RequestExitFromHotkey;
                _hotkeyService.ToggleConsoleRequested -= RequestToggleConsoleFromHotkey;
                _hotkeyService.DisplayConfigurationChanged -= OnHotkeyDisplayConfigurationChanged;
            }
            _instanceService.CommandReceived -= OnInstanceCommandReceived;

            _shutdown.Cancel();
            _captureCoordinator.Stop(TimeSpan.FromSeconds(15));
            WaitForBenchmarkShutdown();
            _consoleUi.PrepareForExit();
        }

        return 0;
    }

    private void ExecuteCommand(ConsoleCommand command)
    {
        switch (command.Action)
        {
            case ConsoleAction.None:
                return;
            case ConsoleAction.Redraw:
                RequestRender();
                return;
            case ConsoleAction.Back:
                Navigate(ConsolePage.Dashboard);
                return;
            case ConsoleAction.CaptureFull:
                QueueCapture(
                    CaptureKind.FullDesktop,
                    Stopwatch.GetTimestamp(),
                    "ConsoleFull");
                return;
            case ConsoleAction.CaptureRegion:
                QueueCapture(
                    CaptureKind.Region,
                    Stopwatch.GetTimestamp(),
                    "ConsoleSnip");
                return;
            case ConsoleAction.OpenCaptureSettings:
                Navigate(ConsolePage.CaptureSettings);
                return;
            case ConsoleAction.OpenHotkeys:
                Navigate(ConsolePage.Hotkeys);
                return;
            case ConsoleAction.OpenSaveLocations:
                Navigate(ConsolePage.SaveLocations);
                return;
            case ConsoleAction.OpenDiagnostics:
                Navigate(ConsolePage.Diagnostics);
                return;
            case ConsoleAction.OpenRecentCaptures:
                RefreshRecentCaptures(showStatus: false);
                Navigate(ConsolePage.RecentCaptures);
                return;
            case ConsoleAction.OpenBackground:
                RefreshAutostartStatus();
                Navigate(ConsolePage.Background);
                return;
            case ConsoleAction.OpenAbout:
                Navigate(ConsolePage.About);
                return;
            case ConsoleAction.ToggleClipboard:
                SaveSettings(ToggleClipboardCopy());
                return;
            case ConsoleAction.ToggleSound:
                SaveSettings(ToggleCaptureSound());
                return;
            case ConsoleAction.ToggleDiagnostics:
                SaveSettings(ToggleDiagnostics());
                return;
            case ConsoleAction.ToggleAutostart:
                ToggleAutostart();
                return;
            case ConsoleAction.HideConsole:
                HideConsole("Console hidden. Use the configured console hotkey or launch SCapturer again to show it.");
                return;
            case ConsoleAction.CycleCaptureBackend:
                SaveSettings(CycleCaptureBackend());
                return;
            case ConsoleAction.EditFullHotkey:
                EditHotkey(HotkeyAction.FullCapture);
                return;
            case ConsoleAction.EditRegionHotkey:
                EditHotkey(HotkeyAction.RegionCapture);
                return;
            case ConsoleAction.EditExitHotkey:
                EditHotkey(HotkeyAction.Exit);
                return;
            case ConsoleAction.EditToggleConsoleHotkey:
                EditHotkey(HotkeyAction.ToggleConsole);
                return;
            case ConsoleAction.RestoreDefaultHotkeys:
                RestoreDefaultHotkeys();
                return;
            case ConsoleAction.ChangeFullFolder:
                ChangeCaptureFolder(CaptureKind.FullDesktop);
                return;
            case ConsoleAction.ChangeSnipFolder:
                ChangeCaptureFolder(CaptureKind.Region);
                return;
            case ConsoleAction.OpenFullFolder:
                OpenCaptureFolder(CaptureKind.FullDesktop);
                return;
            case ConsoleAction.OpenSnipFolder:
                OpenCaptureFolder(CaptureKind.Region);
                return;
            case ConsoleAction.RunBenchmark:
                StartBaselineBenchmark();
                return;
            case ConsoleAction.RunBackendComparison:
                StartBackendComparisonBenchmark();
                return;
            case ConsoleAction.OpenDiagnosticsFolder:
                OpenDiagnosticsFolder();
                return;
            case ConsoleAction.RefreshRecentCaptures:
                RefreshRecentCaptures(showStatus: true);
                return;
            case ConsoleAction.OpenRecentCapture:
                OpenRecentCapture(command.ItemIndex);
                return;
            case ConsoleAction.OpenRecentCaptureFolder:
                OpenRecentCaptureFolder(command.ItemIndex);
                return;
            case ConsoleAction.Exit:
                _shutdown.Cancel();
                return;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(command),
                    command.Action,
                    "Unknown console command.");
        }
    }

    private void Navigate(ConsolePage page)
    {
        _consoleUi.Navigate(page);
        RequestRender();
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

    private string CycleCaptureBackend()
    {
        lock (_uiStateGate)
        {
            _settings.CaptureBackend = _settings.CaptureBackend switch
            {
                CaptureBackendMode.ReferenceGdiPlus => CaptureBackendMode.NativeGdiWic,
                CaptureBackendMode.NativeGdiWic => CaptureBackendMode.Auto,
                _ => CaptureBackendMode.ReferenceGdiPlus,
            };

            var selection = _backendProvider.GetSelection(_settings.CaptureBackend);
            var suffix = selection.IsFallback
                ? $" Fallback: {selection.FallbackReason}"
                : string.Empty;

            return $"Capture backend set to {FormatBackendMode(_settings.CaptureBackend)}; " +
                $"active {selection.ActiveName}.{suffix}";
        }
    }

    private void QueueCapture(
        CaptureKind kind,
        long requestTimestamp,
        string trigger)
    {
        if (Volatile.Read(ref _benchmarkInProgress) == 1)
        {
            SetStatus("A benchmark is running; capture request ignored.");
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
                    $"Benchmark complete ({result.BackendName}). " +
                    $"Median {result.Summary.MedianTotalMilliseconds:0.0} ms; " +
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

    private void StartBackendComparisonBenchmark()
    {
        if (Interlocked.CompareExchange(ref _benchmarkInProgress, 1, 0) != 0)
        {
            SetStatus("A benchmark is already running.");
            return;
        }

        if (_captureCoordinator.HasWork)
        {
            Volatile.Write(ref _benchmarkInProgress, 0);
            SetStatus("Wait for the active capture queue to become idle before benchmarking.");
            return;
        }

        if (!_backendProvider.IsNativeAvailable(out var unavailableReason))
        {
            Volatile.Write(ref _benchmarkInProgress, 0);
            SetStatus($"Native backend comparison unavailable: {unavailableReason}");
            return;
        }

        var settingsSnapshot = GetSettingsSnapshot();
        SetStatus("Starting backend comparison: reference and native, each with 1 warm-up + 10 measured captures.");

        _benchmarkTask = Task.Run(() =>
        {
            try
            {
                var result = _comparisonBenchmarkService.Run(
                    settingsSnapshot,
                    measuredIterations: 10,
                    progress: progress =>
                    {
                        SetStatus(
                            $"{progress.Phase}: {progress.CurrentIteration}/{progress.TotalIterations}.");
                    },
                    cancellationToken: _shutdown.Token);

                string applyMessage;
                try
                {
                    AppSettings settingsToSave;
                    lock (_uiStateGate)
                    {
                        settingsToSave = _settings.CreateSnapshot();
                    }

                    settingsToSave.CaptureBackend = result.Decision.RecommendedMode;
                    _settingsStore.Save(settingsToSave);

                    lock (_uiStateGate)
                    {
                        _settings.CaptureBackend = result.Decision.RecommendedMode;
                    }

                    applyMessage = $" Applied {result.Decision.RecommendedBackend}.";
                }
                catch (Exception exception)
                {
                    applyMessage = $" Recommendation could not be persisted: {exception.Message}";
                }

                SetStatus(
                    $"Backend comparison complete. Recommended: " +
                    $"{result.Decision.RecommendedBackend}. " +
                    $"Native p95 improvement {result.Decision.NativeP95ImprovementPercent:0.0}%; " +
                    $"allocations {result.Decision.NativeAllocationImprovementPercent:0.0}%." +
                    applyMessage + $" Report: {result.ReportFilePath}");
            }
            catch (OperationCanceledException)
            {
                SetStatus("Backend comparison cancelled during shutdown.");
            }
            catch (Exception exception)
            {
                SetStatus($"Backend comparison failed: {exception.Message}");
            }
            finally
            {
                Volatile.Write(ref _benchmarkInProgress, 0);
                RequestRender();
            }
        });
    }

    private void EditHotkey(HotkeyAction action)
    {
        var currentSettings = GetSettingsSnapshot();
        var currentBinding = GetHotkey(currentSettings, action);
        var actionName = HotkeyActionName(action);

        var entered = _consoleUi.PromptForText(
            $"CHANGE {actionName.ToUpperInvariant()} HOTKEY",
            HotkeyBindingService.Format(currentBinding),
            "Type a combination with at least one modifier, such as Ctrl+Alt+G.");

        RequestRender();

        if (string.IsNullOrWhiteSpace(entered))
        {
            SetStatus("Hotkey change cancelled.");
            return;
        }

        if (!HotkeyBindingService.TryParse(
                entered,
                out var parsed,
                out var parseError))
        {
            SetStatus($"Invalid hotkey: {parseError}");
            return;
        }

        var candidate = currentSettings.CreateSnapshot();
        SetHotkey(candidate, action, parsed);

        ApplyHotkeySettings(
            candidate,
            $"{actionName} hotkey changed to {HotkeyBindingService.Format(parsed)}.");
    }

    private void RestoreDefaultHotkeys()
    {
        var candidate = GetSettingsSnapshot();
        candidate.FullCaptureHotkey = HotkeyBinding.CreateDefaultFullCapture();
        candidate.RegionCaptureHotkey = HotkeyBinding.CreateDefaultRegionCapture();
        candidate.ExitHotkey = HotkeyBinding.CreateDefaultExit();
        candidate.ToggleConsoleHotkey = HotkeyBinding.CreateDefaultToggleConsole();

        ApplyHotkeySettings(candidate, "Default hotkeys restored.");
    }

    private void ApplyHotkeySettings(
        AppSettings candidate,
        string successMessage)
    {
        var previous = GetSettingsSnapshot();
        var bindings = HotkeyBindingService.CreateSet(candidate);

        if (!HotkeyBindingService.TryValidateSet(bindings, out var validationError))
        {
            SetStatus($"Hotkey change rejected: {validationError}");
            return;
        }

        var registration = _hotkeyService.TryReconfigure(bindings);
        if (!registration.Success)
        {
            SetStatus($"Hotkey change rejected: {registration.ErrorMessage}");
            return;
        }

        try
        {
            _settingsStore.Save(candidate);

            lock (_uiStateGate)
            {
                _settings.FullCaptureHotkey = candidate.FullCaptureHotkey.CreateSnapshot();
                _settings.RegionCaptureHotkey = candidate.RegionCaptureHotkey.CreateSnapshot();
                _settings.ExitHotkey = candidate.ExitHotkey.CreateSnapshot();
                _settings.ToggleConsoleHotkey =
                    candidate.ToggleConsoleHotkey.CreateSnapshot();
            }

            SetStatus(successMessage);
        }
        catch (Exception exception)
        {
            var rollback = _hotkeyService.TryReconfigure(
                HotkeyBindingService.CreateSet(previous));

            SetStatus(
                rollback.Success
                    ? $"Could not persist hotkeys; previous bindings restored: {exception.Message}"
                    : $"Could not persist hotkeys and rollback failed: {exception.Message}. " +
                      rollback.ErrorMessage);
        }
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

        var recent = _recentCaptureService.FromResult(completed.Result);
        var captureName = completed.Kind == CaptureKind.Region
            ? "region"
            : "full desktop";

        lock (_uiStateGate)
        {
            _lastCapture = completed.Result;

            if (recent is not null)
            {
                _recentCaptures = _recentCaptures
                    .Where(item => !string.Equals(
                        item.FilePath,
                        recent.FilePath,
                        StringComparison.OrdinalIgnoreCase))
                    .Prepend(recent)
                    .Take(12)
                    .ToArray();
            }

            var captureWarnings = completed.Result.Warnings is { Count: > 0 }
                ? " Warning: " + string.Join(
                    " ",
                    completed.Result.Warnings.Select(warning => warning.Message))
                : string.Empty;

            _statusMessage =
                $"Saved {captureName} {completed.Result.Width}×{completed.Result.Height} PNG " +
                $"({FormatBytes(completed.Result.FileSizeBytes)}) via " +
                $"{completed.Result.BackendName} in " +
                $"{completed.Result.Metrics.TotalMilliseconds:0.0} ms: " +
                $"{completed.Result.FilePath}.{captureWarnings}{diagnosticsWarning}";
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

    private void ChangeCaptureFolder(CaptureKind kind)
    {
        var settings = GetSettingsSnapshot();
        var currentFolder = kind == CaptureKind.Region
            ? settings.SnipCaptureFolder
            : settings.FullCaptureFolder;

        var enteredPath = _consoleUi.PromptForText(
            kind == CaptureKind.Region
                ? "CHANGE REGION CAPTURE FOLDER"
                : "CHANGE FULL CAPTURE FOLDER",
            currentFolder,
            "Enter an absolute Windows folder path.");

        RequestRender();

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
                $"{(kind == CaptureKind.Region ? "Region" : "Full capture")} " +
                $"folder changed to: {fullPath}");

            RefreshRecentCaptures(showStatus: false);
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

            OpenFolder(captureFolder);

            SetStatus(
                kind == CaptureKind.Region
                    ? "Region capture folder opened."
                    : "Full capture folder opened.");
        }
        catch (Exception exception)
        {
            SetStatus($"Could not open folder: {exception.Message}");
        }
    }

    private void OpenRecentCapture(int itemIndex)
    {
        var item = GetRecentCapture(itemIndex);
        if (item is null)
        {
            SetStatus("The selected recent capture is no longer available.");
            return;
        }

        try
        {
            if (!File.Exists(item.FilePath))
            {
                RefreshRecentCaptures(showStatus: false);
                SetStatus("The selected capture no longer exists.");
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = item.FilePath,
                UseShellExecute = true,
            });

            SetStatus($"Opened {item.FileName}.");
        }
        catch (Exception exception)
        {
            SetStatus($"Could not open capture: {exception.Message}");
        }
    }

    private void OpenRecentCaptureFolder(int itemIndex)
    {
        var item = GetRecentCapture(itemIndex);
        if (item is null)
        {
            SetStatus("No recent capture is selected.");
            return;
        }

        try
        {
            var folder = Path.GetDirectoryName(item.FilePath);
            if (string.IsNullOrWhiteSpace(folder))
            {
                throw new InvalidOperationException(
                    "The selected capture has no parent folder.");
            }

            OpenFolder(folder);
            SetStatus($"Opened folder for {item.FileName}.");
        }
        catch (Exception exception)
        {
            SetStatus($"Could not open capture folder: {exception.Message}");
        }
    }

    private void RefreshRecentCaptures(bool showStatus)
    {
        var refreshed = _recentCaptureService.Load(
            GetSettingsSnapshot(),
            maximumCount: 12);

        lock (_uiStateGate)
        {
            _recentCaptures = refreshed;
        }

        if (showStatus)
        {
            SetStatus($"Recent captures refreshed: {refreshed.Count} item(s).");
        }
        else
        {
            RequestRender();
        }
    }

    private RecentCaptureItem? GetRecentCapture(int itemIndex)
    {
        lock (_uiStateGate)
        {
            return itemIndex >= 0 && itemIndex < _recentCaptures.Count
                ? _recentCaptures[itemIndex]
                : null;
        }
    }

    private void ToggleAutostart()
    {
        try
        {
            _autostartStatus =
                _autostartStatus.IsEnabled && _autostartStatus.IsCurrent
                    ? _autostartService.Disable()
                    : _autostartService.Enable();

            SetStatus(_autostartStatus.IsEnabled
                ? "Windows autostart enabled. SCapturer will launch hidden after sign-in."
                : "Windows autostart disabled.");
        }
        catch (Exception exception)
        {
            RefreshAutostartStatus();
            SetStatus($"Could not change Windows autostart: {exception.Message}");
        }
    }

    private void RefreshAutostartStatus()
    {
        _autostartStatus = _autostartService.GetStatus();
        RequestRender();
    }

    private void OnInstanceCommandReceived(AppInstanceCommand command)
    {
        QueueExternalCommand(command);
    }

    private void RequestToggleConsoleFromHotkey()
    {
        QueueExternalCommand(AppInstanceCommand.ToggleConsole);
    }

    private void ProcessExternalCommands()
    {
        while (_externalCommands.TryDequeue(out var command))
        {
            switch (command)
            {
                case AppInstanceCommand.ShowConsole:
                    ShowConsole("Console shown by activation request.");
                    break;
                case AppInstanceCommand.HideConsole:
                    HideConsole("Console hidden by activation request.");
                    break;
                case AppInstanceCommand.ToggleConsole:
                    ToggleConsole();
                    break;
                case AppInstanceCommand.CaptureFull:
                    QueueCapture(
                        CaptureKind.FullDesktop,
                        Stopwatch.GetTimestamp(),
                        "InstanceCommandFull");
                    break;
                case AppInstanceCommand.CaptureRegion:
                    QueueCapture(
                        CaptureKind.Region,
                        Stopwatch.GetTimestamp(),
                        "InstanceCommandSnip");
                    break;
                case AppInstanceCommand.CancelRegion:
                    _captureCoordinator.CancelActiveRegion();
                    SetStatus("Active region selection cancellation requested.");
                    break;
                case AppInstanceCommand.Exit:
                    SetStatus("Exit command received. Finishing active file operations.");
                    _shutdown.Cancel();
                    break;
                case AppInstanceCommand.None:
                default:
                    break;
            }
        }
    }

    private void ToggleConsole()
    {
        if (_consoleVisibility.IsVisible)
        {
            HideConsole(
                "Console hidden. Use the configured console hotkey or launch SCapturer again to show it.");
            return;
        }

        ShowConsole("Console shown.");
    }

    private void ShowConsole(string status)
    {
        if (_consoleVisibility.Show())
        {
            _consoleUi.Invalidate();
            SetStatus(status);
        }
    }

    private void HideConsole(string status)
    {
        SetStatus(status);
        _consoleVisibility.Hide();
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

    private ConsoleViewModel CreateViewModel()
    {
        lock (_uiStateGate)
        {
            var settings = _settings.CreateSnapshot();
            return new ConsoleViewModel(
                Settings: settings,
                StatusMessage: _statusMessage,
                LastCapture: _lastCapture,
                Pipeline: _pipelineSnapshot,
                Topology: _displayTopology.GetSnapshot(),
                BackendSelection: _backendProvider.GetSelection(settings.CaptureBackend),
                ConsoleVisible: _consoleVisibility.IsVisible,
                StartedInBackground: _startedInBackground,
                Autostart: _autostartStatus,
                BenchmarkInProgress: Volatile.Read(ref _benchmarkInProgress) == 1,
                RecentCaptures: _recentCaptures.ToArray());
        }
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

    private static HotkeyBinding GetHotkey(
        AppSettings settings,
        HotkeyAction action)
    {
        return action switch
        {
            HotkeyAction.FullCapture => settings.FullCaptureHotkey,
            HotkeyAction.RegionCapture => settings.RegionCaptureHotkey,
            HotkeyAction.Exit => settings.ExitHotkey,
            HotkeyAction.ToggleConsole => settings.ToggleConsoleHotkey,
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };
    }

    private static void SetHotkey(
        AppSettings settings,
        HotkeyAction action,
        HotkeyBinding binding)
    {
        switch (action)
        {
            case HotkeyAction.FullCapture:
                settings.FullCaptureHotkey = binding.CreateSnapshot();
                break;
            case HotkeyAction.RegionCapture:
                settings.RegionCaptureHotkey = binding.CreateSnapshot();
                break;
            case HotkeyAction.Exit:
                settings.ExitHotkey = binding.CreateSnapshot();
                break;
            case HotkeyAction.ToggleConsole:
                settings.ToggleConsoleHotkey = binding.CreateSnapshot();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(action));
        }
    }

    private static string HotkeyActionName(HotkeyAction action)
    {
        return action switch
        {
            HotkeyAction.FullCapture => "Full capture",
            HotkeyAction.RegionCapture => "Region capture",
            HotkeyAction.Exit => "Exit",
            HotkeyAction.ToggleConsole => "Toggle console",
            _ => action.ToString(),
        };
    }

    private static void OpenFolder(string folder)
    {
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo
        {
            FileName = folder,
            UseShellExecute = true,
        });
    }

    private void OpenDiagnosticsFolder()
    {
        try
        {
            OpenFolder(_paths.DiagnosticsDirectory);
            SetStatus("Diagnostics folder opened.");
        }
        catch (Exception exception)
        {
            SetStatus($"Could not open diagnostics folder: {exception.Message}");
        }
    }

    private static string FormatBackendMode(CaptureBackendMode mode)
    {
        return mode switch
        {
            CaptureBackendMode.Auto => "Auto",
            CaptureBackendMode.ReferenceGdiPlus => "Reference GDI+",
            CaptureBackendMode.NativeGdiWic => "Native GDI + WIC",
            _ => mode.ToString(),
        };
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
