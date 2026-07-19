namespace XLab.ScreenCaptureTool.Models;

internal sealed class AppSettings
{
    public string FullCaptureFolder { get; set; } = CreateDefaultCaptureFolder();

    public bool CopyToClipboard { get; set; } = true;

    public bool PlayCaptureSound { get; set; } = true;

    public static AppSettings CreateDefault() => new();

    private static string CreateDefaultCaptureFolder()
    {
        var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if (string.IsNullOrWhiteSpace(pictures))
        {
            pictures = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        return Path.Combine(pictures, "X-LAB Screenshots", "Full");
    }
}
