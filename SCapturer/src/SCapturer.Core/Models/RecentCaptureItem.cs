namespace SCapturer.Core.Models;

public sealed record RecentCaptureItem(
    string FilePath,
    CaptureKind Kind,
    DateTimeOffset LastWriteTime,
    long FileSizeBytes)
{
    public string FileName => Path.GetFileName(FilePath);
}
