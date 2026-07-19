using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SCapturer.Core.Capture;
using SCapturer.Core.Diagnostics;
using SCapturer.Core.Display;
using SCapturer.Core.Models;
using SCapturer.Core.Pipeline;

namespace SCapturer.Core.Services;

public sealed class CaptureService
{
    private const int MaximumTopologyAttempts = 2;

    private readonly DisplayTopologyService _displayTopology;
    private readonly CaptureBackendProvider _backendProvider;

    public CaptureService(
        DisplayTopologyService displayTopology,
        CaptureBackendProvider backendProvider)
    {
        _displayTopology = displayTopology;
        _backendProvider = backendProvider;
    }

    public CaptureResult CaptureFullDesktop(
        AppSettings settings,
        long requestTimestamp = 0,
        string trigger = "Console",
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

        var backend = _backendProvider.GetBackend(settings.CaptureBackend);

        stageChanged?.Invoke(CapturePipelineStage.DirectoryPreparation);
        var stageStarted = Stopwatch.GetTimestamp();
        Directory.CreateDirectory(settings.FullCaptureFolder);
        var filePath = CreateUniqueFilePath(settings.FullCaptureFolder, "Screenshot");
        var directoryPreparationMilliseconds = ElapsedMilliseconds(stageStarted);

        CaptureFrame? frame = null;
        DisplayTopologySnapshot? topology = null;
        var bitmapAllocationMilliseconds = 0d;
        var pixelAcquisitionMilliseconds = 0d;

        try
        {
            for (var attempt = 1; attempt <= MaximumTopologyAttempts; attempt++)
            {
                topology = _displayTopology.AcquireStableSnapshot();

                var capture = backend.Capture(
                    topology.VirtualBounds,
                    phase => stageChanged?.Invoke(phase switch
                    {
                        CaptureBackendPhase.BufferAllocation =>
                            CapturePipelineStage.BitmapAllocation,
                        CaptureBackendPhase.PixelAcquisition =>
                            CapturePipelineStage.PixelAcquisition,
                        _ => CapturePipelineStage.PixelAcquisition,
                    }));

                frame = capture.Frame;
                bitmapAllocationMilliseconds += capture.BufferAllocationMilliseconds;
                pixelAcquisitionMilliseconds += capture.PixelAcquisitionMilliseconds;

                if (_displayTopology.IsCurrent(topology.Version))
                {
                    break;
                }

                frame.Dispose();
                frame = null;

                if (attempt == MaximumTopologyAttempts)
                {
                    throw new DisplayTopologyChangedException(
                        "The display topology changed repeatedly during full-desktop capture.");
                }
            }

            if (frame is null || topology is null)
            {
                throw new InvalidOperationException(
                    "The full-desktop capture frame was not initialized.");
            }

            stageChanged?.Invoke(CapturePipelineStage.PngPersistence);
            stageStarted = Stopwatch.GetTimestamp();
            backend.SavePng(frame, filePath);
            var fileInfo = new FileInfo(filePath);
            var pngPersistenceMilliseconds = ElapsedMilliseconds(stageStarted);

            var clipboardMilliseconds = 0d;
            if (settings.CopyToClipboard)
            {
                stageChanged?.Invoke(CapturePipelineStage.ClipboardPublication);
                stageStarted = Stopwatch.GetTimestamp();
                SetClipboardImageWithRetry(frame.Bitmap);
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
                Width: frame.Width,
                Height: frame.Height,
                FileSizeBytes: fileInfo.Length,
                Metrics: metrics,
                DesktopContext: CreateDesktopContext(topology),
                BackendKind: frame.BackendKind,
                BackendName: frame.BackendName);
        }
        finally
        {
            frame?.Dispose();
        }
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

    private static double ElapsedMilliseconds(
        long startedTimestamp,
        long finishedTimestamp)
    {
        return Stopwatch.GetElapsedTime(
            startedTimestamp,
            finishedTimestamp).TotalMilliseconds;
    }
}
