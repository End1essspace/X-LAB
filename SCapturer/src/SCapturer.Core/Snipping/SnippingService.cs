using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SCapturer.Core.Diagnostics;
using SCapturer.Core.Models;
using SCapturer.Core.Pipeline;

namespace SCapturer.Core.Snipping;

public sealed class SnippingService
{
    private readonly object _overlayGate = new();

    private SnipOverlayForm? _activeOverlay;
    private bool _cancelRequested;

    public CaptureResult? CaptureRegion(
        AppSettings settings,
        long requestTimestamp = 0,
        string trigger = "ConsoleSnip",
        Action<CapturePipelineStage>? stageChanged = null)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var startedAtUtc = DateTimeOffset.UtcNow;
        var workingSetBefore = Environment.WorkingSet;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var operationStarted = Stopwatch.GetTimestamp();
        var dispatchMilliseconds = requestTimestamp > 0
            ? ElapsedMilliseconds(requestTimestamp, operationStarted)
            : 0;

        var bounds = SystemInformation.VirtualScreen;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            throw new InvalidOperationException("Windows reported an invalid virtual desktop size.");
        }

        stageChanged?.Invoke(CapturePipelineStage.DirectoryPreparation);
        var stageStarted = Stopwatch.GetTimestamp();
        Directory.CreateDirectory(settings.SnipCaptureFolder);
        var filePath = CreateUniqueFilePath(settings.SnipCaptureFolder, "Snip");
        var directoryPreparationMilliseconds = ElapsedMilliseconds(stageStarted);

        stageChanged?.Invoke(CapturePipelineStage.BitmapAllocation);
        stageStarted = Stopwatch.GetTimestamp();
        using var desktopFrame = new Bitmap(
            bounds.Width,
            bounds.Height,
            PixelFormat.Format32bppPArgb);
        var bitmapAllocationMilliseconds = ElapsedMilliseconds(stageStarted);

        stageChanged?.Invoke(CapturePipelineStage.PixelAcquisition);
        stageStarted = Stopwatch.GetTimestamp();
        using (var graphics = Graphics.FromImage(desktopFrame))
        {
            graphics.CopyFromScreen(
                sourceX: bounds.Left,
                sourceY: bounds.Top,
                destinationX: 0,
                destinationY: 0,
                blockRegionSize: bounds.Size,
                copyPixelOperation: CopyPixelOperation.SourceCopy);
        }

        var pixelAcquisitionMilliseconds = ElapsedMilliseconds(stageStarted);

        stageChanged?.Invoke(CapturePipelineStage.OverlayPreparation);
        stageStarted = Stopwatch.GetTimestamp();
        using var overlay = new SnipOverlayForm(desktopFrame, bounds);
        RegisterOverlay(overlay);
        var overlayPreparationMilliseconds = ElapsedMilliseconds(stageStarted);

        try
        {
            stageChanged?.Invoke(CapturePipelineStage.RegionSelection);
            stageStarted = Stopwatch.GetTimestamp();

            if (IsCancellationRequested())
            {
                overlay.RequestCancel();
            }

            var dialogResult = overlay.ShowDialog();
            var interactionMilliseconds = ElapsedMilliseconds(stageStarted);

            if (dialogResult != DialogResult.OK || overlay.SelectedRegion.IsEmpty)
            {
                stageChanged?.Invoke(CapturePipelineStage.Cancelled);
                return null;
            }

            var selectedRegion = overlay.SelectedRegion;

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

            return new CaptureResult(
                FilePath: filePath,
                Width: croppedImage.Width,
                Height: croppedImage.Height,
                FileSizeBytes: fileInfo.Length,
                Metrics: metrics,
                Kind: CaptureKind.Region,
                Region: new CaptureRegion(
                    X: bounds.Left + selectedRegion.X,
                    Y: bounds.Top + selectedRegion.Y,
                    Width: selectedRegion.Width,
                    Height: selectedRegion.Height),
                SnipMetrics: new SnipCaptureMetrics(
                    OverlayPreparationMilliseconds: overlayPreparationMilliseconds,
                    InteractionMilliseconds: interactionMilliseconds,
                    CropMilliseconds: cropMilliseconds));
        }
        finally
        {
            UnregisterOverlay(overlay);
        }
    }

    public void CancelActiveSelection()
    {
        SnipOverlayForm? overlay;

        lock (_overlayGate)
        {
            _cancelRequested = true;
            overlay = _activeOverlay;
        }

        overlay?.RequestCancel();
    }

    private void RegisterOverlay(SnipOverlayForm overlay)
    {
        lock (_overlayGate)
        {
            _activeOverlay = overlay;

            if (_cancelRequested)
            {
                overlay.RequestCancel();
            }
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

            _cancelRequested = false;
        }
    }

    private bool IsCancellationRequested()
    {
        lock (_overlayGate)
        {
            return _cancelRequested;
        }
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
