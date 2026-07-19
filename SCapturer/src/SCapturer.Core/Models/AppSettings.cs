namespace SCapturer.Core.Models;

public sealed class AppSettings
{
    public string FullCaptureFolder { get; set; } = CreateDefaultCaptureFolder("Full");

    public string SnipCaptureFolder { get; set; } = CreateDefaultCaptureFolder("Snips");

    public bool CopyToClipboard { get; set; } = true;

    public bool PlayCaptureSound { get; set; } = true;

    public bool EnableDiagnostics { get; set; }

    public static AppSettings CreateDefault() => new();

    public AppSettings CreateSnapshot()
    {
        return new AppSettings
        {
            FullCaptureFolder = FullCaptureFolder,
            SnipCaptureFolder = SnipCaptureFolder,
            CopyToClipboard = CopyToClipboard,
            PlayCaptureSound = PlayCaptureSound,
            EnableDiagnostics = EnableDiagnostics,
        };
    }

    private static string CreateDefaultCaptureFolder(string captureType)
    {
        var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if (string.IsNullOrWhiteSpace(pictures))
        {
            pictures = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        return Path.Combine(pictures, "SCapturer", captureType);
    }
}
