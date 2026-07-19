namespace XLab.ScreenCaptureTool.Models;

internal sealed record CaptureResult(
    string FilePath,
    int Width,
    int Height,
    long FileSizeBytes);
