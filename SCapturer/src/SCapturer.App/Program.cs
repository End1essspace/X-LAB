using System.Text;
using System.Windows.Forms;
using SCapturer.App.UI;
using SCapturer.Core.Benchmarking;
using SCapturer.Core.Diagnostics;
using SCapturer.Core.Display;
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
            var captureService = new CaptureService(displayTopology);
            using var snippingService = new SnippingService(displayTopology);
            var diagnosticsStore = new CaptureDiagnosticsStore(paths);
            var benchmarkService = new BaselineBenchmarkService(captureService, paths);
            using var captureCoordinator = new CaptureCoordinator(
                captureService,
                snippingService);
            var consoleUi = new ConsoleUi(paths);
            var app = new AppController(
                settingsStore,
                captureCoordinator,
                diagnosticsStore,
                benchmarkService,
                displayTopology,
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
