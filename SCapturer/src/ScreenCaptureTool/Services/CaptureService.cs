using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using XLab.ScreenCaptureTool.Models;

namespace XLab.ScreenCaptureTool.Services;

internal sealed class CaptureService
{
    public CaptureResult CaptureFullDesktop(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var bounds = SystemInformation.VirtualScreen;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            throw new InvalidOperationException("Windows reported an invalid virtual desktop size.");
        }

        Directory.CreateDirectory(settings.FullCaptureFolder);
        var filePath = CreateUniqueFilePath(settings.FullCaptureFolder, "Screenshot");

        using var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppPArgb);
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

        bitmap.Save(filePath, ImageFormat.Png);

        if (settings.CopyToClipboard)
        {
            SetClipboardImageWithRetry(bitmap);
        }

        if (settings.PlayCaptureSound)
        {
            System.Media.SystemSounds.Asterisk.Play();
        }

        var fileInfo = new FileInfo(filePath);
        return new CaptureResult(filePath, bounds.Width, bounds.Height, fileInfo.Length);
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
}
