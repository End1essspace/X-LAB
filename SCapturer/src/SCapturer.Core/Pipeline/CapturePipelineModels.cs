using SCapturer.Core.Models;

namespace SCapturer.Core.Pipeline;

public enum CapturePipelineState
{
    Idle,
    Queued,
    Capturing,
    PreparingOverlay,
    Selecting,
    Cropping,
    Saving,
    Publishing,
    Finalizing,
    Completed,
    Cancelled,
    Failed,
    Stopping,
    Stopped,
}

public enum CapturePipelineStage
{
    DirectoryPreparation,
    BitmapAllocation,
    PixelAcquisition,
    OverlayPreparation,
    RegionSelection,
    RegionCropping,
    PngPersistence,
    ClipboardPublication,
    SoundDispatch,
    Completed,
    Cancelled,
}

public enum CaptureEnqueueResult
{
    Accepted,
    Coalesced,
    Stopping,
}

public enum CaptureCancellationReason
{
    User,
    DisplayTopologyChanged,
    Shutdown,
}

public sealed record CapturePipelineSnapshot(
    long Version,
    CapturePipelineState State,
    bool HasActiveRequest,
    bool HasPendingRequest,
    CaptureKind? ActiveKind,
    CaptureKind? PendingKind,
    string? ActiveTrigger,
    string? PendingTrigger)
{
    public static CapturePipelineSnapshot Initial { get; } = new(
        Version: 0,
        State: CapturePipelineState.Idle,
        HasActiveRequest: false,
        HasPendingRequest: false,
        ActiveKind: null,
        PendingKind: null,
        ActiveTrigger: null,
        PendingTrigger: null);

    public bool HasWork => HasActiveRequest || HasPendingRequest;
}

public sealed record CaptureCompletedEvent(
    long RequestId,
    CaptureKind Kind,
    string Trigger,
    AppSettings Settings,
    CaptureResult Result);

public sealed record CaptureCancelledEvent(
    long RequestId,
    CaptureKind Kind,
    string Trigger,
    CaptureCancellationReason Reason);

public sealed record CaptureFailedEvent(
    long RequestId,
    CaptureKind Kind,
    string Trigger,
    Exception Exception);
