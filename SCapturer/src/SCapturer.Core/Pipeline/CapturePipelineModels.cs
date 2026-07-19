using SCapturer.Core.Models;

namespace SCapturer.Core.Pipeline;

public enum CapturePipelineState
{
    Idle,
    Queued,
    Capturing,
    Saving,
    Publishing,
    Finalizing,
    Completed,
    Failed,
    Stopping,
    Stopped,
}

public enum CapturePipelineStage
{
    DirectoryPreparation,
    BitmapAllocation,
    PixelAcquisition,
    PngPersistence,
    ClipboardPublication,
    SoundDispatch,
    Completed,
}

public enum CaptureEnqueueResult
{
    Accepted,
    Coalesced,
    Stopping,
}

public sealed record CapturePipelineSnapshot(
    long Version,
    CapturePipelineState State,
    bool HasActiveRequest,
    bool HasPendingRequest,
    string? ActiveTrigger,
    string? PendingTrigger)
{
    public static CapturePipelineSnapshot Initial { get; } = new(
        Version: 0,
        State: CapturePipelineState.Idle,
        HasActiveRequest: false,
        HasPendingRequest: false,
        ActiveTrigger: null,
        PendingTrigger: null);

    public bool HasWork => HasActiveRequest || HasPendingRequest;
}

public sealed record CaptureCompletedEvent(
    long RequestId,
    string Trigger,
    AppSettings Settings,
    CaptureResult Result);

public sealed record CaptureFailedEvent(
    long RequestId,
    string Trigger,
    Exception Exception);
