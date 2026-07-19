using System.Drawing;
using SCapturer.Core.Display;

namespace SCapturer.Core.Capture;

public enum CaptureBackendMode
{
    Auto,
    ReferenceGdiPlus,
    NativeGdiWic,
}

public enum CaptureBackendKind
{
    ReferenceGdiPlus,
    NativeGdiWic,
}

public enum CaptureBackendPhase
{
    BufferAllocation,
    PixelAcquisition,
}

public sealed record CaptureBackendSelection(
    CaptureBackendMode RequestedMode,
    CaptureBackendKind ActiveKind,
    string ActiveName,
    bool IsFallback,
    string? FallbackReason);

public sealed record CaptureBackendCaptureResult(
    CaptureFrame Frame,
    double BufferAllocationMilliseconds,
    double PixelAcquisitionMilliseconds);

public abstract class CaptureFrame : IDisposable
{
    public abstract int Width { get; }

    public abstract int Height { get; }

    public abstract int Stride { get; }

    public abstract Bitmap Bitmap { get; }

    public abstract CaptureBackendKind BackendKind { get; }

    public abstract string BackendName { get; }

    public abstract void Dispose();
}

public interface ICaptureBackend
{
    CaptureBackendKind Kind { get; }

    string Name { get; }

    bool IsAvailable(out string? reason);

    CaptureBackendCaptureResult Capture(
        PhysicalRectangle bounds,
        Action<CaptureBackendPhase>? phaseChanged = null);

    CaptureFrame Crop(CaptureFrame source, Rectangle region);

    void SavePng(CaptureFrame frame, string filePath);
}
