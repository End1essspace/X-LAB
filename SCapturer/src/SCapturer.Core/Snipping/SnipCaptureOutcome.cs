using SCapturer.Core.Models;
using SCapturer.Core.Pipeline;

namespace SCapturer.Core.Snipping;

public sealed record SnipCaptureOutcome(
    CaptureResult? Result,
    CaptureCancellationReason? CancellationReason)
{
    public static SnipCaptureOutcome Completed(CaptureResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new SnipCaptureOutcome(result, CancellationReason: null);
    }

    public static SnipCaptureOutcome Cancelled(CaptureCancellationReason reason)
    {
        return new SnipCaptureOutcome(Result: null, CancellationReason: reason);
    }
}
