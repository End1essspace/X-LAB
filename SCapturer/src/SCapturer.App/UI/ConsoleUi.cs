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
        Console.WriteLine(" Performance-first Windows screenshot utility — P4 rectangular snipping");
        WriteRule();
        WritePair("Listener", "ACTIVE");
        WritePair("Pipeline", FormatPipeline(pipeline));
        WritePair("Benchmark", benchmarkInProgress ? "RUNNING" : "IDLE");
        WritePair("Full capture", "Ctrl + Shift + G");
        WritePair("Region capture", "Ctrl + Shift + S");
        WritePair("Exit", "Ctrl + Shift + Q");
        WritePair("Format", "PNG (lossless)");
        WritePair("Clipboard", OnOff(settings.CopyToClipboard));
        WritePair("Sound", OnOff(settings.PlayCaptureSound));
        WritePair("Diagnostics", OnOff(settings.EnableDiagnostics));
        WritePair("Full folder", settings.FullCaptureFolder);
        WritePair("Snip folder", settings.SnipCaptureFolder);

        if (lastCapture is not null)
        {
            var kind = lastCapture.Kind == CaptureKind.Region
                ? "REGION"
                : "FULL";

            var timing = lastCapture.Kind == CaptureKind.Region
                ? $"select {(lastCapture.SnipMetrics?.InteractionMilliseconds ?? 0):0.0} ms | " +
                  $"crop {(lastCapture.SnipMetrics?.CropMilliseconds ?? 0):0.0} ms | " +
                  $"PNG {lastCapture.Metrics.PngPersistenceMilliseconds:0.0} ms"
                : $"dispatch {lastCapture.Metrics.DispatchMilliseconds:0.0} ms | " +
                  $"pixels {lastCapture.Metrics.PixelAcquisitionMilliseconds:0.0} ms | " +
                  $"PNG {lastCapture.Metrics.PngPersistenceMilliseconds:0.0} ms";

            WritePair(
                "Last capture",
                $"{kind} {lastCapture.Width}×{lastCapture.Height} | " +
                $"{timing} | total {lastCapture.Metrics.TotalMilliseconds:0.0} ms");
        }

        WriteRule();
        Console.WriteLine(" [1] Queue full desktop capture");
        Console.WriteLine(" [2] Open rectangular snipping overlay");
        Console.WriteLine(" [3] Change full capture folder");
        Console.WriteLine(" [4] Change snip capture folder");
        Console.WriteLine(" [5] Open full capture folder");
        Console.WriteLine(" [6] Open snip capture folder");
        Console.WriteLine(" [7] Toggle clipboard copy");
        Console.WriteLine(" [8] Toggle capture sound");
        Console.WriteLine(" [9] Toggle capture diagnostics");
        Console.WriteLine(" [B] Run full-capture baseline benchmark");
        Console.WriteLine(" [0] Exit");
        WriteRule();
        Console.WriteLine(" Snipping: drag with left mouse; Esc or right-click cancels.");
        Console.WriteLine($" Status: {statusMessage}");
        Console.WriteLine($" Config: {_paths.SettingsFile}");
        Console.WriteLine($" Metrics: {_paths.CaptureMetricsFile}");
        Console.WriteLine($" Benchmarks: {_paths.BenchmarkReportsDirectory}");
        WriteRule();
        Console.Write(" Select an option: ");
    }

    public string? PromptForFolder(
        string title,
        string currentFolder)
    {
        Console.Clear();
        WriteRule(title);
        Console.WriteLine($" Current: {currentFolder}");
        Console.WriteLine();
        Console.WriteLine(" Enter an absolute path. Leave empty to cancel.");
        Console.Write("> ");
        return Console.ReadLine();
    }

    private static void WritePair(string label, string value)
    {
        Console.WriteLine($" {label,-15}: {value}");
    }

    private static string FormatPipeline(CapturePipelineSnapshot pipeline)
    {
        var state = pipeline.State.ToString().ToUpperInvariant();
        var activeKind = pipeline.ActiveKind is null
            ? string.Empty
            : $" {FormatKind(pipeline.ActiveKind.Value)}";

        if (pipeline.HasPendingRequest)
        {
            var pendingKind = pipeline.PendingKind is null
                ? string.Empty
                : $" {FormatKind(pipeline.PendingKind.Value)}";

            return $"{state}{activeKind} + 1 PENDING{pendingKind}";
        }

        return state + activeKind;
    }

    private static string FormatKind(CaptureKind kind)
    {
        return kind == CaptureKind.Region ? "REGION" : "FULL";
    }

    private static void WriteRule(string? title = null)
    {
        const int width = 108;

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
