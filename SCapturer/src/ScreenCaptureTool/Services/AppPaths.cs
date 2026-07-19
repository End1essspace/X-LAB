namespace XLab.ScreenCaptureTool.Services;

internal sealed class AppPaths
{
    public AppPaths()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        DataDirectory = Path.Combine(localAppData, "X-LAB", "ScreenCaptureTool");
        SettingsFile = Path.Combine(DataDirectory, "config.json");
    }

    public string DataDirectory { get; }

    public string SettingsFile { get; }
}
