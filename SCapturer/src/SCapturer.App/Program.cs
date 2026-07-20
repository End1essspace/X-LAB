using System.Windows.Forms;
using SCapturer.App.Lifecycle;
using SCapturer.App.UI;
using SCapturer.Core.Benchmarking;
using SCapturer.Core.Capture;
using SCapturer.Core.Diagnostics;
using SCapturer.Core.Display;
using SCapturer.Core.Persistence;
using SCapturer.Core.Pipeline;
using SCapturer.Core.Services;
using SCapturer.Core.Snipping;

namespace SCapturer.App;

internal static class Program
{
    private const string DisableHotkeysEnvironmentVariable =
        "SCAPTURER_DISABLE_HOTKEYS";
    private const string NonInteractiveEnvironmentVariable =
        "SCAPTURER_NONINTERACTIVE";

    [STAThread]
    private static int Main(string[] args)
    {
        var launchOptions = AppLaunchOptions.Parse(args);
        var consoleVisibility = new ConsoleVisibilityService();
        var globalHotkeysEnabled = !IsEnvironmentFlagEnabled(
            DisableHotkeysEnvironmentVariable);
        var nonInteractive = IsEnvironmentFlagEnabled(
            NonInteractiveEnvironmentVariable);

        if (!launchOptions.IsValid)
        {
            consoleVisibility.Show();
            Console.Error.WriteLine(launchOptions.ErrorMessage);
            return 4;
        }

        if (launchOptions.ResumeAfterProcessId is int previousProcessId)
        {
            ConsoleCloseHandoffService.WaitForPreviousProcess(previousProcessId);
        }

        if (launchOptions.RemoveAutostart)
        {
            try
            {
                _ = new AutostartService().Disable();
            }
            catch (Exception exception)
                when (exception is IOException or
                      UnauthorizedAccessException or
                      System.Security.SecurityException)
            {
                if (!launchOptions.IsMaintenanceCommand)
                {
                    consoleVisibility.Show();
                    Console.Error.WriteLine(exception.Message);
                }

                return 7;
            }
        }

        var highDpiConfigured = Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        if (!highDpiConfigured && Application.HighDpiMode != HighDpiMode.PerMonitorV2)
        {
            consoleVisibility.Show();
            Console.Error.WriteLine(
                "SCapturer could not enable Per-Monitor V2 DPI awareness.");
            return 3;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        using var instanceMutex = new Mutex(
            initiallyOwned: true,
            name: AppInstanceService.MutexName,
            createdNew: out var isFirstInstance);

        if (!isFirstInstance)
        {
            if (AppInstanceService.TrySend(
                    launchOptions.SecondaryCommand,
                    TimeSpan.FromSeconds(3),
                    out var activationError))
            {
                if (!launchOptions.WaitForSecondaryExit)
                {
                    return 0;
                }

                return WaitForPrimaryInstanceExit(
                    instanceMutex,
                    TimeSpan.FromSeconds(45),
                    out var waitError)
                    ? 0
                    : ReportMaintenanceFailure(
                        launchOptions,
                        consoleVisibility,
                        waitError,
                        exitCode: 5);
            }

            return ReportMaintenanceFailure(
                launchOptions,
                consoleVisibility,
                activationError,
                exitCode: 2);
        }

        if (launchOptions.PrimaryCommand == AppInstanceCommand.Exit)
        {
            return 0;
        }

        using var consoleCloseHandoff =
            new ConsoleCloseHandoffService(consoleVisibility);

        if (!launchOptions.StartHidden && !consoleVisibility.Show())
        {
            return 6;
        }

        try
        {
            using var instanceService = new AppInstanceService();
            instanceService.Start();

            var paths = new AppPaths();
            var settingsStore = new SettingsStore(paths);
            var autostartService = new AutostartService();
            using var displayTopology = new DisplayTopologyService();
            var backendProvider = new CaptureBackendProvider();
            var persistenceService = new CapturePersistenceService();
            using var clipboardService = new ClipboardPublicationService();
            var captureService = new CaptureService(
                displayTopology,
                backendProvider,
                persistenceService,
                clipboardService);
            using var snippingService = new SnippingService(
                displayTopology,
                backendProvider,
                persistenceService,
                clipboardService);
            var diagnosticsStore = new CaptureDiagnosticsStore(paths);
            var benchmarkService = new BaselineBenchmarkService(captureService, paths);
            var comparisonBenchmarkService = new BackendComparisonBenchmarkService(
                benchmarkService,
                backendProvider,
                paths);
            using var captureCoordinator = new CaptureCoordinator(
                captureService,
                snippingService);
            using var hotkeyService = new HotkeyService();
            var recentCaptureService = new RecentCaptureService();
            var consoleUi = new ConsoleUi(paths);

            var app = new AppController(
                paths,
                settingsStore,
                autostartService,
                captureCoordinator,
                diagnosticsStore,
                benchmarkService,
                comparisonBenchmarkService,
                backendProvider,
                displayTopology,
                hotkeyService,
                recentCaptureService,
                consoleUi,
                consoleVisibility,
                instanceService,
                launchOptions.StartHidden,
                globalHotkeysEnabled);

            if (launchOptions.PrimaryCommand != AppInstanceCommand.None)
            {
                app.QueueExternalCommand(launchOptions.PrimaryCommand);
            }

            return app.Run();
        }
        catch (Exception exception)
        {
            consoleVisibility.Show();
            Console.Error.WriteLine();
            Console.Error.WriteLine("Fatal error:");
            Console.Error.WriteLine(exception);
            Console.Error.WriteLine();
            if (!nonInteractive)
            {
                Console.Error.WriteLine("Press any key to close.");
                Console.ReadKey(intercept: true);
            }

            return 1;
        }
    }

    private static bool WaitForPrimaryInstanceExit(
        Mutex instanceMutex,
        TimeSpan timeout,
        out string? errorMessage)
    {
        var acquired = false;
        errorMessage = null;

        try
        {
            try
            {
                acquired = instanceMutex.WaitOne(timeout);
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            if (!acquired)
            {
                errorMessage =
                    "The running SCapturer instance did not finish graceful shutdown " +
                    $"within {timeout.TotalSeconds:0} seconds.";
                return false;
            }

            return true;
        }
        finally
        {
            if (acquired)
            {
                instanceMutex.ReleaseMutex();
            }
        }
    }

    private static int ReportMaintenanceFailure(
        AppLaunchOptions launchOptions,
        ConsoleVisibilityService consoleVisibility,
        string? message,
        int exitCode)
    {
        if (!launchOptions.IsMaintenanceCommand)
        {
            consoleVisibility.Show();
            Console.Error.WriteLine(message);
        }

        return exitCode;
    }

    private static bool IsEnvironmentFlagEnabled(string variableName)
    {
        var value = Environment.GetEnvironmentVariable(variableName);
        return value is not null &&
            (value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
             value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
             value.Equals("yes", StringComparison.OrdinalIgnoreCase));
    }
}
