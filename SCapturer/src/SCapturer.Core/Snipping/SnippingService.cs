using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SCapturer.Core.Diagnostics;
using SCapturer.Core.Display;
using SCapturer.Core.Models;
using SCapturer.Core.Pipeline;

namespace SCapturer.Core.Snipping;

public sealed class SnippingService : IDisposable
{
    private const int MaximumTopologyAttempts = 2;

    private readonly DisplayTopologyService _displayTopology;
    private readonly object _overlayGate = new();

    private SnipOverlayForm? _activeOverlay;
    private CaptureCancellationReason? _cancelReason;
    private long? _activeTopologyVersion;
    private bool _captureActive;
    private bool _disposed;

    public SnippingService(DisplayTopologyService displayTopology)
    {
        _displayTopology = displayTopology;
        _displayTopology.TopologyChanged += OnTopologyChanged;
    }

    public SnipCaptureOutcome CaptureRegion(
        AppSettings settings,
        long requestTimestamp = 0,
        string trigger = "ConsoleSnip",
        Action<CapturePipelineStage>? stageChanged = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ObjectDisposedException.ThrowIf(_disposed, this);

        BeginCapture();

        Bitmap? desktopFrame = null;

        try
        {
            var startedAtUtc = DateTimeOffset.UtcNow;
            var workingSetBefore = Environment.WorkingSet;
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var operationStarted = Stopwatch.GetTimestamp();
            var dispatchMilliseconds = requestTimestamp > 0
                ? ElapsedMilliseconds(requestTimestamp, operationStarted)
                : 0;

            stageChanged?.Invoke(CapturePipelineStage.DirectoryPreparation);
            var stageStarted = Stopwatch.GetTimestamp();
            Directory.CreateDirectory(settings.SnipCaptureFolder);
            var filePath = CreateUniqueFilePath(settings.SnipCaptureFolder, "Snip");
            var directoryPreparationMilliseconds = ElapsedMilliseconds(stageStarted);

            DisplayTopologySnapshot? topology = null;
            var bitmapAllocationMilliseconds = 0d;
            var pixelAcquisitionMilliseconds = 0d;

            for (var attempt = 1; attempt <= MaximumTopologyAttempts; attempt++)
            {
                var earlyCancellation = GetCancellationReason();
                if (earlyCancellation is not null)
                {
                    stageChanged?.Invoke(CapturePipelineStage.Cancelled);
                    return SnipCaptureOutcome.Cancelled(earlyCancellation.Value);
                }

                topology = _displayTopology.AcquireStableSnapshot();
                SetActiveTopologyVersion(topology.Version);
                var bounds = topology.VirtualBounds;

                stageChanged?.Invoke(CapturePipelineStage.BitmapAllocation);
                stageStarted = Stopwatch.GetTimestamp();
                desktopFrame = new Bitmap(
                    bounds.Width,
                    bounds.Height,
                    PixelFormat.Format32bppPArgb);
                bitmapAllocationMilliseconds += ElapsedMilliseconds(stageStarted);

                stageChanged?.Invoke(CapturePipelineStage.PixelAcquisition);
                stageStarted = Stopwatch.GetTimestamp();
                using (var graphics = Graphics.FromImage(desktopFrame))
                {
                    graphics.CopyFromScreen(
                        sourceX: bounds.Left,
                        sourceY: bounds.Top,
                        destinationX: 0,
                        destinationY: 0,
                        blockRegionSize: bounds.ToRectangle().Size,
                        copyPixelOperation: CopyPixelOperation.SourceCopy);
                }

                pixelAcquisitionMilliseconds += ElapsedMilliseconds(stageStarted);

                var cancellationAfterCapture = GetCancellationReason();
                if (cancellationAfterCapture is not null)
                {
                    stageChanged?.Invoke(CapturePipelineStage.Cancelled);
                    return SnipCaptureOutcome.Cancelled(cancellationAfterCapture.Value);
                }

                if (_displayTopology.IsCurrent(topology.Version))
                {
                    break;
                }

                desktopFrame.Dispose();
                desktopFrame = null;

                if (attempt == MaximumTopologyAttempts)
                {
                    stageChanged?.Invoke(CapturePipelineStage.Cancelled);
                    return SnipCaptureOutcome.Cancelled(
                        CaptureCancellationReason.DisplayTopologyChanged);
                }
            }

            if (desktopFrame is null || topology is null)
            {
                throw new InvalidOperationException(
                    "The snipping desktop frame was not initialized.");
            }

            stageChanged?.Invoke(CapturePipelineStage.OverlayPreparation);
            stageStarted = Stopwatch.GetTimestamp();
            using var overlay = new SnipOverlayForm(desktopFrame, topology);
            overlay.DisplayConfigurationChanged += OnOverlayDisplayConfigurationChanged;
            RegisterOverlay(overlay, topology.Version);
            var overlayPreparationMilliseconds = ElapsedMilliseconds(stageStarted);

            try
            {
                stageChanged?.Invoke(CapturePipelineStage.RegionSelection);
                stageStarted = Stopwatch.GetTimestamp();

                var pendingCancellation = GetCancellationReason();
                if (pendingCancellation is not null)
                {
                    overlay.RequestCancel();
                }

                var dialogResult = overlay.ShowDialog();
                var interactionMilliseconds = ElapsedMilliseconds(stageStarted);

                if (dialogResult != DialogResult.OK || overlay.SelectedRegion.IsEmpty)
                {
                    stageChanged?.Invoke(CapturePipelineStage.Cancelled);
                    return SnipCaptureOutcome.Cancelled(
                        ResolveCancellationReason(overlay));
                }

                if (!_displayTopology.IsCurrent(topology.Version))
                {
                    stageChanged?.Invoke(CapturePipelineStage.Cancelled);
                    return SnipCaptureOutcome.Cancelled(
                        CaptureCancellationReason.DisplayTopologyChanged);
                }

                var selectedRegion = Rectangle.Intersect(
                    new Rectangle(Point.Empty, desktopFrame.Size),
                    overlay.SelectedRegion);

                if (selectedRegion.Width < 2 || selectedRegion.Height < 2)
                {
                    stageChanged?.Invoke(CapturePipelineStage.Cancelled);
                    return SnipCaptureOutcome.Cancelled(
                        CaptureCancellationReason.User);
                }

                stageChanged?.Invoke(CapturePipelineStage.RegionCropping);
                stageStarted = Stopwatch.GetTimestamp();

                using var croppedImage = new Bitmap(
                    selectedRegion.Width,
                    selectedRegion.Height,
                    PixelFormat.Format32bppPArgb);

                using (var cropGraphics = Graphics.FromImage(croppedImage))
                {
                    cropGraphics.DrawImage(
                        desktopFrame,
                        new Rectangle(0, 0, croppedImage.Width, croppedImage.Height),
                        selectedRegion,
                        GraphicsUnit.Pixel);
                }

                var cropMilliseconds = ElapsedMilliseconds(stageStarted);

                stageChanged?.Invoke(CapturePipelineStage.PngPersistence);
                stageStarted = Stopwatch.GetTimestamp();
                croppedImage.Save(filePath, ImageFormat.Png);
                var fileInfo = new FileInfo(filePath);
                var pngPersistenceMilliseconds = ElapsedMilliseconds(stageStarted);

                var clipboardMilliseconds = 0d;
                if (settings.CopyToClipboard)
                {
                    stageChanged?.Invoke(CapturePipelineStage.ClipboardPublication);
                    stageStarted = Stopwatch.GetTimestamp();
                    SetClipboardImageWithRetry(croppedImage);
                    clipboardMilliseconds = ElapsedMilliseconds(stageStarted);
                }

                var soundMilliseconds = 0d;
                if (settings.PlayCaptureSound)
                {
                    stageChanged?.Invoke(CapturePipelineStage.SoundDispatch);
                    stageStarted = Stopwatch.GetTimestamp();
                    System.Media.SystemSounds.Asterisk.Play();
                    soundMilliseconds = ElapsedMilliseconds(stageStarted);
                }

                var operationFinished = Stopwatch.GetTimestamp();
                var allocatedAfter = GC.GetAllocatedBytesForCurrentThread();
                var workingSetAfter = Environment.WorkingSet;

                var metrics = new CaptureMetrics(
                    StartedAtUtc: startedAtUtc,
                    Trigger: trigger,
                    DispatchMilliseconds: dispatchMilliseconds,
                    DirectoryPreparationMilliseconds: directoryPreparationMilliseconds,
                    BitmapAllocationMilliseconds: bitmapAllocationMilliseconds,
                    PixelAcquisitionMilliseconds: pixelAcquisitionMilliseconds,
                    PngPersistenceMilliseconds: pngPersistenceMilliseconds,
                    ClipboardMilliseconds: clipboardMilliseconds,
                    SoundMilliseconds: soundMilliseconds,
                    TotalMilliseconds: ElapsedMilliseconds(operationStarted, operationFinished),
                    ManagedAllocatedBytes: Math.Max(0, allocatedAfter - allocatedBefore),
                    WorkingSetBeforeBytes: workingSetBefore,
                    WorkingSetAfterBytes: workingSetAfter);

                stageChanged?.Invoke(CapturePipelineStage.Completed);

                return SnipCaptureOutcome.Completed(new CaptureResult(
                    FilePath: filePath,
                    Width: croppedImage.Width,
                    Height: croppedImage.Height,
                    FileSizeBytes: fileInfo.Length,
                    Metrics: metrics,
                    Kind: CaptureKind.Region,
                    Region: new CaptureRegion(
                        X: topology.VirtualBounds.Left + selectedRegion.X,
                        Y: topology.VirtualBounds.Top + selectedRegion.Y,
                        Width: selectedRegion.Width,
                        Height: selectedRegion.Height),
                    SnipMetrics: new SnipCaptureMetrics(
                        OverlayPreparationMilliseconds: overlayPreparationMilliseconds,
                        InteractionMilliseconds: interactionMilliseconds,
                        CropMilliseconds: cropMilliseconds),
                    DesktopContext: CreateDesktopContext(topology)));
            }
            finally
            {
                overlay.DisplayConfigurationChanged -= OnOverlayDisplayConfigurationChanged;
                UnregisterOverlay(overlay);
            }
        }
        finally
        {
            desktopFrame?.Dispose();
            EndCapture();
        }
    }

    public void CancelActiveSelection(CaptureCancellationReason reason)
    {
        SnipOverlayForm? overlay;

        lock (_overlayGate)
        {
            if (!_captureActive)
            {
                return;
            }

            _cancelReason = HigherPriorityReason(_cancelReason, reason);
            overlay = _activeOverlay;
        }

        overlay?.RequestCancel();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _displayTopology.TopologyChanged -= OnTopologyChanged;
        CancelActiveSelection(CaptureCancellationReason.Shutdown);
    }

    private void OnTopologyChanged(DisplayTopologyChange change)
    {
        bool shouldCancel;

        lock (_overlayGate)
        {
            shouldCancel = _captureActive &&
                (!_activeTopologyVersion.HasValue ||
                 change.Version != _activeTopologyVersion.Value ||
                 !change.IsStable);
        }

        if (shouldCancel)
        {
            CancelActiveSelection(CaptureCancellationReason.DisplayTopologyChanged);
        }
    }

    private void OnOverlayDisplayConfigurationChanged()
    {
        _displayTopology.NotifyExternalChange(
            "The snipping overlay received WM_DISPLAYCHANGE");
        CancelActiveSelection(CaptureCancellationReason.DisplayTopologyChanged);
    }

    private void BeginCapture()
    {
        lock (_overlayGate)
        {
            _captureActive = true;
            _activeOverlay = null;
            _activeTopologyVersion = null;
            _cancelReason = null;
        }
    }

    private void EndCapture()
    {
        lock (_overlayGate)
        {
            _captureActive = false;
            _activeOverlay = null;
            _activeTopologyVersion = null;
            _cancelReason = null;
        }
    }

    private void SetActiveTopologyVersion(long version)
    {
        lock (_overlayGate)
        {
            _activeTopologyVersion = version;
        }
    }

    private void RegisterOverlay(SnipOverlayForm overlay, long topologyVersion)
    {
        CaptureCancellationReason? cancellation;

        lock (_overlayGate)
        {
            _activeOverlay = overlay;
            _activeTopologyVersion = topologyVersion;
            cancellation = _cancelReason;
        }

        if (cancellation is not null)
        {
            overlay.RequestCancel();
        }
    }

    private void UnregisterOverlay(SnipOverlayForm overlay)
    {
        lock (_overlayGate)
        {
            if (ReferenceEquals(_activeOverlay, overlay))
            {
                _activeOverlay = null;
            }
        }
    }

    private CaptureCancellationReason ResolveCancellationReason(
        SnipOverlayForm overlay)
    {
        if (overlay.DisplayChangeDetected)
        {
            return CaptureCancellationReason.DisplayTopologyChanged;
        }

        return GetCancellationReason() ?? CaptureCancellationReason.User;
    }

    private CaptureCancellationReason? GetCancellationReason()
    {
        lock (_overlayGate)
        {
            return _cancelReason;
        }
    }

    private static CaptureCancellationReason HigherPriorityReason(
        CaptureCancellationReason? current,
        CaptureCancellationReason incoming)
    {
        if (current is null)
        {
            return incoming;
        }

        static int Priority(CaptureCancellationReason reason)
        {
            return reason switch
            {
                CaptureCancellationReason.Shutdown => 3,
                CaptureCancellationReason.DisplayTopologyChanged => 2,
                _ => 1,
            };
        }

        return Priority(incoming) > Priority(current.Value)
            ? incoming
            : current.Value;
    }

    private static CaptureDesktopContext CreateDesktopContext(
        DisplayTopologySnapshot topology)
    {
        return new CaptureDesktopContext(
            TopologyVersion: topology.Version,
            MonitorCount: topology.MonitorCount,
            VirtualBounds: topology.VirtualBounds,
            IsRemoteSession: topology.IsRemoteSession,
            DpiMode: topology.DpiMode);
    }

    private static string CreateUniqueFilePath(string directory, string prefix)
    {
        var baseName = $"{prefix}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss-fff}";
        var candidate = Path.Combine(directory, baseName + ".png");
        var suffix = 1;

        while (File.Exists(candidate))
        {
            candidate = Path.Combine(directory, $"{baseName}_{suffix++}.png");
        }

        return candidate;
    }

    private static void SetClipboardImageWithRetry(Image image)
    {
        const int attempts = 6;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                Clipboard.SetImage(image);
                return;
            }
            catch (ExternalException) when (attempt < attempts)
            {
                Thread.Sleep(50 * attempt);
            }
        }
    }

    private static double ElapsedMilliseconds(long startedTimestamp)
    {
        return ElapsedMilliseconds(startedTimestamp, Stopwatch.GetTimestamp());
    }

    private static double ElapsedMilliseconds(long startedTimestamp, long finishedTimestamp)
    {
        return Stopwatch.GetElapsedTime(startedTimestamp, finishedTimestamp).TotalMilliseconds;
    }
}
