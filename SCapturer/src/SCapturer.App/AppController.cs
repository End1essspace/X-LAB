using System.Diagnostics;
using SCapturer.App.UI;
using SCapturer.Core.Models;
using SCapturer.Core.Services;

namespace SCapturer.App;

internal sealed class AppController
{
    private readonly SettingsStore _settingsStore;
    private readonly CaptureService _captureService;
    private readonly ConsoleUi _consoleUi;
    private readonly CancellationTokenSource _shutdown = new();

    private AppSettings _settings;
    private string _statusMessage = "Ready.";
    private int _captureInProgress;
    private int _renderRequested = 1;

    public AppController(
        SettingsStore settingsStore,
        CaptureService captureService,
        ConsoleUi consoleUi)
    {
        _settingsStore = settingsStore;
        _captureService = captureService;
        _consoleUi = consoleUi;
        _settings = _settingsStore.Load();
    }

    public int Run()
    {
        using var hotkeys = new HotkeyService();
        hotkeys.FullCaptureRequested += CaptureFromHotkey;
        hotkeys.ExitRequested += RequestExitFromHotkey;
        hotkeys.Start();

        _statusMessage = "Listener active. Ctrl+Shift+G captures; Ctrl+Shift+Q exits.";
        RequestRender();

        while (!_shutdown.IsCancellationRequested)
        {
            if (Interlocked.Exchange(ref _renderRequested, 0) == 1)
            {
                _consoleUi.RenderMainMenu(_settings, _statusMessage);
            }

            if (Console.KeyAvailable)
            {
                var key = Console.ReadKey(intercept: true);
                HandleMenuKey(key.KeyChar);
            }

            Thread.Sleep(40);
        }

        return 0;
    }

    private void HandleMenuKey(char key)
    {
        switch (key)
        {
            case '1':
                CaptureFullDesktop();
                break;
            case '2':
                ChangeCaptureFolder();
                break;
            case '3':
                OpenCaptureFolder();
                break;
            case '4':
                _settings.CopyToClipboard = !_settings.CopyToClipboard;
                SaveSettings($"Clipboard copy {EnabledText(_settings.CopyToClipboard)}.");
                break;
            case '5':
                _settings.PlayCaptureSound = !_settings.PlayCaptureSound;
                SaveSettings($"Capture sound {EnabledText(_settings.PlayCaptureSound)}.");
                break;
            case '0':
                _shutdown.Cancel();
                break;
            default:
                _statusMessage = $"Unknown option: {key}";
                RequestRender();
                break;
        }
    }

    private void CaptureFromHotkey()
    {
        CaptureFullDesktop();
    }

    private void CaptureFullDesktop()
    {
        if (Interlocked.Exchange(ref _captureInProgress, 1) == 1)
        {
            _statusMessage = "A capture is already in progress.";
            RequestRender();
            return;
        }

        try
        {
            var result = _captureService.CaptureFullDesktop(_settings);
            _statusMessage =
                $"Saved {result.Width}×{result.Height} PNG ({FormatBytes(result.FileSizeBytes)}): {result.FilePath}";
        }
        catch (Exception exception)
        {
            _statusMessage = $"Capture failed: {exception.Message}";
        }
        finally
        {
            Volatile.Write(ref _captureInProgress, 0);
            RequestRender();
        }
    }

    private void ChangeCaptureFolder()
    {
        var enteredPath = _consoleUi.PromptForFolder(_settings.FullCaptureFolder);
        if (string.IsNullOrWhiteSpace(enteredPath))
        {
            _statusMessage = "Folder change cancelled.";
            RequestRender();
            return;
        }

        try
        {
            var expanded = Environment.ExpandEnvironmentVariables(enteredPath.Trim().Trim('"'));
            var fullPath = Path.GetFullPath(expanded);
            Directory.CreateDirectory(fullPath);
            _settings.FullCaptureFolder = fullPath;
            SaveSettings($"Capture folder changed to: {fullPath}");
        }
        catch (Exception exception)
        {
            _statusMessage = $"Invalid folder: {exception.Message}";
            RequestRender();
        }
    }

    private void OpenCaptureFolder()
    {
        try
        {
            Directory.CreateDirectory(_settings.FullCaptureFolder);
            Process.Start(new ProcessStartInfo
            {
                FileName = _settings.FullCaptureFolder,
                UseShellExecute = true,
            });

            _statusMessage = "Capture folder opened.";
        }
        catch (Exception exception)
        {
            _statusMessage = $"Could not open folder: {exception.Message}";
        }

        RequestRender();
    }

    private void RequestExitFromHotkey()
    {
        _statusMessage = "Exit hotkey received.";
        _shutdown.Cancel();
    }

    private void SaveSettings(string successMessage)
    {
        try
        {
            _settingsStore.Save(_settings);
            _statusMessage = successMessage;
        }
        catch (Exception exception)
        {
            _statusMessage = $"Could not save settings: {exception.Message}";
        }

        RequestRender();
    }

    private void RequestRender()
    {
        Interlocked.Exchange(ref _renderRequested, 1);
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
