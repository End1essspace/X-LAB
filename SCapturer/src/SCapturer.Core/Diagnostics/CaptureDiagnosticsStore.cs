using System.Text.Json;
using SCapturer.Core.Models;
using SCapturer.Core.Services;

namespace SCapturer.Core.Diagnostics;

public sealed class CaptureDiagnosticsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly AppPaths _paths;
    private readonly object _writeLock = new();

    public CaptureDiagnosticsStore(AppPaths paths)
    {
        _paths = paths;
    }

    public void Record(CaptureResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var entry = new CaptureDiagnosticEntry(
            RecordedAtUtc: DateTimeOffset.UtcNow,
            Kind: result.Kind,
            FilePath: result.FilePath,
            Width: result.Width,
            Height: result.Height,
            FileSizeBytes: result.FileSizeBytes,
            Region: result.Region,
            SnipMetrics: result.SnipMetrics,
            DesktopContext: result.DesktopContext,
            BackendKind: result.BackendKind,
            BackendName: result.BackendName,
            Metrics: result.Metrics);

        var json = JsonSerializer.Serialize(entry, JsonOptions);

        lock (_writeLock)
        {
            Directory.CreateDirectory(_paths.DiagnosticsDirectory);
            File.AppendAllText(_paths.CaptureMetricsFile, json + Environment.NewLine);
        }
    }

    private sealed record CaptureDiagnosticEntry(
        DateTimeOffset RecordedAtUtc,
        CaptureKind Kind,
        string FilePath,
        int Width,
        int Height,
        long FileSizeBytes,
        CaptureRegion? Region,
        SnipCaptureMetrics? SnipMetrics,
        CaptureDesktopContext? DesktopContext,
        SCapturer.Core.Capture.CaptureBackendKind BackendKind,
        string BackendName,
        CaptureMetrics Metrics);
}
