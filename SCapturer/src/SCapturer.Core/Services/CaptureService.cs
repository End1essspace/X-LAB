using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SCapturer.Core.Diagnostics;
using SCapturer.Core.Models;

namespace SCapturer.Core.Services;

public sealed class CaptureService
{
    public CaptureResult CaptureFullDesktop(
        AppSettings settings,
        long requestTimestamp = 0,
        string trigger = "Console")
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

        var stageStarted = Stopwatch.GetTimestamp();
        Directory.CreateDirectory(settings.FullCaptureFolder);
        var filePath = CreateUniqueFilePath(settings.FullCaptureFolder, "Screenshot");
        var directoryPreparationMilliseconds = ElapsedMilliseconds(stageStarted);

        stageStarted = Stopwatch.GetTimestamp();
        using var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppPArgb);
        var bitmapAllocationMilliseconds = ElapsedMilliseconds(stageStarted);

        stageStarted = Stopwatch.GetTimestamp();
        using (var graphics = Graphics.FromImage(bitmap))
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

        stageStarted = Stopwatch.GetTimestamp();
        bitmap.Save(filePath, ImageFormat.Png);
        var fileInfo = new FileInfo(filePath);
        var pngPersistenceMilliseconds = ElapsedMilliseconds(stageStarted);

        var clipboardMilliseconds = 0d;
        if (settings.CopyToClipboard)
        {
            stageStarted = Stopwatch.GetTimestamp();
            SetClipboardImageWithRetry(bitmap);
            clipboardMilliseconds = ElapsedMilliseconds(stageStarted);
        }

        var soundMilliseconds = 0d;
        if (settings.PlayCaptureSound)
        {
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

        return new CaptureResult(
            filePath,
            bounds.Width,
            bounds.Height,
            fileInfo.Length,
            metrics);
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
