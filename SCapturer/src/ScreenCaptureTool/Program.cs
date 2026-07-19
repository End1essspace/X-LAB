using System.Text;
using System.Windows.Forms;
using XLab.ScreenCaptureTool.Services;
using XLab.ScreenCaptureTool.UI;

namespace XLab.ScreenCaptureTool;

internal static class Program
{
    private const string MutexName = @"Local\XLab.ScreenCaptureTool";

    [STAThread]
    private static int Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;
        Console.Title = "X-LAB Screen Capture";

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        using var instanceMutex = new Mutex(
            initiallyOwned: true,
            name: MutexName,
            createdNew: out var isFirstInstance);

        if (!isFirstInstance)
        {
            Console.Error.WriteLine("X-LAB Screen Capture is already running.");
            return 2;
        }

        try
        {
            var paths = new AppPaths();
            var settingsStore = new SettingsStore(paths);
            var captureService = new CaptureService();
            var consoleUi = new ConsoleUi(paths);
            var app = new AppController(settingsStore, captureService, consoleUi);

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
