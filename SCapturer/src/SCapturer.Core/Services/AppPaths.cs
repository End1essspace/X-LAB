namespace SCapturer.Core.Services;

public sealed class AppPaths
{
    public const string DataDirectoryEnvironmentVariable = "SCAPTURER_DATA_DIRECTORY";

    public AppPaths(
        string? dataDirectory = null,
        string? legacyDataDirectory = null)
    {
        var localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);

        DataDirectory = NormalizeRoot(
            dataDirectory ?? Environment.GetEnvironmentVariable(
                DataDirectoryEnvironmentVariable),
            Path.Combine(localAppData, "SCapturer"));
        SettingsFile = Path.Combine(DataDirectory, "config.json");

        DiagnosticsDirectory = Path.Combine(DataDirectory, "diagnostics");
        CaptureMetricsFile = Path.Combine(
            DiagnosticsDirectory,
            "capture-metrics.jsonl");
        BenchmarkReportsDirectory = Path.Combine(
            DiagnosticsDirectory,
            "benchmarks");

        LegacyDataDirectory = NormalizeRoot(
            legacyDataDirectory,
            Path.Combine(localAppData, "X-LAB", "ScreenCaptureTool"));
        LegacySettingsFile = Path.Combine(
            LegacyDataDirectory,
            "config.json");
    }

    public string DataDirectory { get; }

    public string SettingsFile { get; }

    public string DiagnosticsDirectory { get; }

    public string CaptureMetricsFile { get; }

    public string BenchmarkReportsDirectory { get; }

    public string LegacyDataDirectory { get; }

    public string LegacySettingsFile { get; }

    private static string NormalizeRoot(
        string? configured,
        string fallback)
    {
        var value = string.IsNullOrWhiteSpace(configured)
            ? fallback
            : Environment.ExpandEnvironmentVariables(
                configured.Trim().Trim('"'));

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));
    }
}
