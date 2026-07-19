
using SCapturer.Core.Diagnostics;

namespace SCapturer.Core.Models;

public sealed record CaptureResult(
    string FilePath,
    int Width,
    int Height,
    long FileSizeBytes,
    CaptureMetrics Metrics);
