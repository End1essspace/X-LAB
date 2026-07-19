namespace SCapturer.Core.Diagnostics;

public sealed record CaptureMetrics(
    DateTimeOffset StartedAtUtc,
    string Trigger,
    double DispatchMilliseconds,
    double DirectoryPreparationMilliseconds,
    double BitmapAllocationMilliseconds,
    double PixelAcquisitionMilliseconds,
    double PngPersistenceMilliseconds,
    double ClipboardMilliseconds,
    double SoundMilliseconds,
    double TotalMilliseconds,
    long ManagedAllocatedBytes,
    long WorkingSetBeforeBytes,
    long WorkingSetAfterBytes);
