using System.Text;
using System.Windows.Forms;
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
    private const string MutexName = @"Local\SCapturer.App";

    [STAThread]
    private static int Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;
        Console.Title = "SCapturer";

        var highDpiConfigured = Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        if (!highDpiConfigured && Application.HighDpiMode != HighDpiMode.PerMonitorV2)
        {
            Console.Error.WriteLine(
                "SCapturer could not enable Per-Monitor V2 DPI awareness.");
            return 3;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        using var instanceMutex = new Mutex(
            initiallyOwned: true,
            name: MutexName,
            createdNew: out var isFirstInstance);

        if (!isFirstInstance)
        {
            Console.Error.WriteLine("SCapturer is already running.");
            return 2;
        }

        try
        {
            var paths = new AppPaths();
            var settingsStore = new SettingsStore(paths);
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
                captureCoordinator,
                diagnosticsStore,
                benchmarkService,
                comparisonBenchmarkService,
                backendProvider,
                displayTopology,
                hotkeyService,
                recentCaptureService,
                consoleUi);

            return app.Run();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("Fatal error:");
            Console.Error.WriteLine(exception);
            Console.Error.WriteLine();
            Console.Error.WriteLine("Press any key to close.");
            Console.ReadKey(intercept: true);
            return 1;
        }
    }
}
