using SCapturer.Core.Models;
using SCapturer.Core.Pipeline;
using SCapturer.Core.Services;

namespace SCapturer.App.UI;

internal sealed class ConsoleUi
{
    private readonly AppPaths _paths;

    public ConsoleUi(AppPaths paths)
    {
        _paths = paths;
    }

    public void RenderMainMenu(
        AppSettings settings,
        string statusMessage,
        CaptureResult? lastCapture,
        CapturePipelineSnapshot pipeline,
        bool benchmarkInProgress)
    {
        Console.Clear();
        WriteRule("SCAPTURER");
        Console.WriteLine(" Performance-first Windows screenshot utility — P3 async pipeline");
        WriteRule();
        WritePair("Listener", "ACTIVE");
        WritePair("Pipeline", FormatPipeline(pipeline));
        WritePair("Benchmark", benchmarkInProgress ? "RUNNING" : "IDLE");
        WritePair("Full capture", "Ctrl + Shift + G");
        WritePair("Exit", "Ctrl + Shift + Q");
        WritePair("Format", "PNG (lossless)");
        WritePair("Clipboard", OnOff(settings.CopyToClipboard));
        WritePair("Sound", OnOff(settings.PlayCaptureSound));
        WritePair("Diagnostics", OnOff(settings.EnableDiagnostics));
        WritePair("Save folder", settings.FullCaptureFolder);

        if (lastCapture is not null)
        {
            WritePair(
                "Last capture",
                $"dispatch {lastCapture.Metrics.DispatchMilliseconds:0.0} ms | " +
                $"pixels {lastCapture.Metrics.PixelAcquisitionMilliseconds:0.0} ms | " +
                $"PNG {lastCapture.Metrics.PngPersistenceMilliseconds:0.0} ms | " +
                $"total {lastCapture.Metrics.TotalMilliseconds:0.0} ms");
        }

        WriteRule();
        Console.WriteLine(" [1] Queue full desktop capture");
        Console.WriteLine(" [2] Change save folder");
        Console.WriteLine(" [3] Open save folder");
        Console.WriteLine(" [4] Toggle clipboard copy");
        Console.WriteLine(" [5] Toggle capture sound");
        Console.WriteLine(" [6] Toggle capture diagnostics");
        Console.WriteLine(" [7] Run baseline benchmark (non-blocking UI)");
        Console.WriteLine(" [0] Exit");
        WriteRule();
        Console.WriteLine($" Status: {statusMessage}");
        Console.WriteLine($" Config: {_paths.SettingsFile}");
        Console.WriteLine($" Metrics: {_paths.CaptureMetricsFile}");
        Console.WriteLine($" Benchmarks: {_paths.BenchmarkReportsDirectory}");
        WriteRule();
        Console.Write(" Select an option: ");
    }

    public string? PromptForFolder(string currentFolder)
    {
        Console.Clear();
        WriteRule("CHANGE SAVE FOLDER");
        Console.WriteLine($" Current: {currentFolder}");
        Console.WriteLine();
        Console.WriteLine(" Enter an absolute path. Leave empty to cancel.");
        Console.Write("> ");
        return Console.ReadLine();
    }

    private static void WritePair(string label, string value)
    {
        Console.WriteLine($" {label,-14}: {value}");
    }

    private static string FormatPipeline(CapturePipelineSnapshot pipeline)
    {
        var state = pipeline.State.ToString().ToUpperInvariant();

        if (pipeline.HasPendingRequest)
        {
            return $"{state} + 1 PENDING";
        }

        return state;
    }

    private static void WriteRule(string? title = null)
    {
        const int width = 96;

        if (string.IsNullOrWhiteSpace(title))
        {
            Console.WriteLine(new string('─', width));
            return;
        }

        var content = $" {title} ";
        var remaining = Math.Max(0, width - content.Length);
        var left = remaining / 2;
        var right = remaining - left;
        Console.WriteLine(new string('─', left) + content + new string('─', right));
    }

    private static string OnOff(bool value) => value ? "ON" : "OFF";
}
