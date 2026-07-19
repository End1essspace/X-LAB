namespace SCapturer.Core.Services;

public sealed class AppPaths
{
    public AppPaths()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        DataDirectory = Path.Combine(localAppData, "SCapturer");
        SettingsFile = Path.Combine(DataDirectory, "config.json");

        DiagnosticsDirectory = Path.Combine(DataDirectory, "diagnostics");
        CaptureMetricsFile = Path.Combine(DiagnosticsDirectory, "capture-metrics.jsonl");
        BenchmarkReportsDirectory = Path.Combine(DiagnosticsDirectory, "benchmarks");

        LegacyDataDirectory = Path.Combine(localAppData, "X-LAB", "ScreenCaptureTool");
        LegacySettingsFile = Path.Combine(LegacyDataDirectory, "config.json");
    }

    public string DataDirectory { get; }

    public string SettingsFile { get; }

    public string DiagnosticsDirectory { get; }

    public string CaptureMetricsFile { get; }

    public string BenchmarkReportsDirectory { get; }

    public string LegacyDataDirectory { get; }

    public string LegacySettingsFile { get; }
}
