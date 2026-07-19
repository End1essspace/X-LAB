namespace SCapturer.Core.Models;

public sealed class AppSettings
{
    public string FullCaptureFolder { get; set; } = CreateDefaultCaptureFolder("Full");

    public string SnipCaptureFolder { get; set; } = CreateDefaultCaptureFolder("Snips");

    public bool CopyToClipboard { get; set; } = true;

    public bool PlayCaptureSound { get; set; } = true;

    public bool EnableDiagnostics { get; set; }

    public HotkeyBinding FullCaptureHotkey { get; set; } =
        HotkeyBinding.CreateDefaultFullCapture();

    public HotkeyBinding RegionCaptureHotkey { get; set; } =
        HotkeyBinding.CreateDefaultRegionCapture();

    public HotkeyBinding ExitHotkey { get; set; } =
        HotkeyBinding.CreateDefaultExit();

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
            FullCaptureHotkey = FullCaptureHotkey.CreateSnapshot(),
            RegionCaptureHotkey = RegionCaptureHotkey.CreateSnapshot(),
            ExitHotkey = ExitHotkey.CreateSnapshot(),
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
