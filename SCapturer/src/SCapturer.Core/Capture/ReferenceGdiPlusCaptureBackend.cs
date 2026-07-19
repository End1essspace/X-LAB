using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using SCapturer.Core.Display;

namespace SCapturer.Core.Capture;

internal sealed class ReferenceGdiPlusCaptureBackend : ICaptureBackend
{
    public CaptureBackendKind Kind => CaptureBackendKind.ReferenceGdiPlus;

    public string Name => "Reference GDI+";

    public bool IsAvailable(out string? reason)
    {
        reason = null;
        return true;
    }

    public CaptureBackendCaptureResult Capture(
        PhysicalRectangle bounds,
        Action<CaptureBackendPhase>? phaseChanged = null)
    {
        if (bounds.IsEmpty)
        {
            throw new ArgumentException("Capture bounds cannot be empty.", nameof(bounds));
        }

        phaseChanged?.Invoke(CaptureBackendPhase.BufferAllocation);
        var stageStarted = Stopwatch.GetTimestamp();
        var bitmap = new Bitmap(
            bounds.Width,
            bounds.Height,
            PixelFormat.Format32bppPArgb);
        var allocationMilliseconds = ElapsedMilliseconds(stageStarted);

        try
        {
            phaseChanged?.Invoke(CaptureBackendPhase.PixelAcquisition);
            stageStarted = Stopwatch.GetTimestamp();

            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(
                    sourceX: bounds.Left,
                    sourceY: bounds.Top,
                    destinationX: 0,
                    destinationY: 0,
                    blockRegionSize: bounds.ToRectangle().Size,
                    copyPixelOperation: CopyPixelOperation.SourceCopy);
            }

            var acquisitionMilliseconds = ElapsedMilliseconds(stageStarted);

            return new CaptureBackendCaptureResult(
                new ReferenceCaptureFrame(bitmap),
                allocationMilliseconds,
                acquisitionMilliseconds);
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    public CaptureFrame Crop(CaptureFrame source, Rectangle region)
    {
        ArgumentNullException.ThrowIfNull(source);
        ValidateRegion(source, region);

        var cropped = new Bitmap(
            region.Width,
            region.Height,
            PixelFormat.Format32bppPArgb);

        try
        {
            using var graphics = Graphics.FromImage(cropped);
            graphics.DrawImage(
                source.Bitmap,
                new Rectangle(0, 0, cropped.Width, cropped.Height),
                region,
                GraphicsUnit.Pixel);

            return new ReferenceCaptureFrame(cropped);
        }
        catch
        {
            cropped.Dispose();
            throw;
        }
    }

    public void SavePng(CaptureFrame frame, string filePath)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        frame.Bitmap.Save(filePath, ImageFormat.Png);
    }

    private static void ValidateRegion(CaptureFrame source, Rectangle region)
    {
        var frameBounds = new Rectangle(0, 0, source.Width, source.Height);
        if (region.Width <= 0 ||
            region.Height <= 0 ||
            Rectangle.Intersect(frameBounds, region) != region)
        {
            throw new ArgumentOutOfRangeException(
                nameof(region),
                region,
                "The crop rectangle must be fully contained in the source frame.");
        }
    }

    private static double ElapsedMilliseconds(long startedTimestamp)
    {
        return Stopwatch.GetElapsedTime(startedTimestamp).TotalMilliseconds;
    }

    private sealed class ReferenceCaptureFrame : CaptureFrame
    {
        private Bitmap? _bitmap;

        public ReferenceCaptureFrame(Bitmap bitmap)
        {
            _bitmap = bitmap;
        }

        public override int Width => GetBitmap().Width;

        public override int Height => GetBitmap().Height;

        public override int Stride => checked(Width * 4);

        public override Bitmap Bitmap => GetBitmap();

        public override CaptureBackendKind BackendKind =>
            CaptureBackendKind.ReferenceGdiPlus;

        public override string BackendName => "Reference GDI+";

        public override void Dispose()
        {
            Interlocked.Exchange(ref _bitmap, null)?.Dispose();
        }

        private Bitmap GetBitmap()
        {
            return _bitmap ?? throw new ObjectDisposedException(
                nameof(ReferenceCaptureFrame));
        }
    }
}
