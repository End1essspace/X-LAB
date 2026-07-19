using SCapturer.Core.Diagnostics;
using SCapturer.Core.Display;

namespace SCapturer.Core.Models;

public enum CaptureKind
{
    FullDesktop,
    Region,
}

public sealed record CaptureRegion(
    int X,
    int Y,
    int Width,
    int Height);

public sealed record SnipCaptureMetrics(
    double OverlayPreparationMilliseconds,
    double InteractionMilliseconds,
    double CropMilliseconds);

public sealed record CaptureDesktopContext(
    long TopologyVersion,
    int MonitorCount,
    PhysicalRectangle VirtualBounds,
    bool IsRemoteSession,
    string DpiMode);

public sealed record CaptureResult(
    string FilePath,
    int Width,
    int Height,
    long FileSizeBytes,
    CaptureMetrics Metrics,
    CaptureKind Kind = CaptureKind.FullDesktop,
    CaptureRegion? Region = null,
    SnipCaptureMetrics? SnipMetrics = null,
    CaptureDesktopContext? DesktopContext = null);
