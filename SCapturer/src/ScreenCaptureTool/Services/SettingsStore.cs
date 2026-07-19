using System.Text.Json;
using XLab.ScreenCaptureTool.Models;

namespace XLab.ScreenCaptureTool.Services;

internal sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly AppPaths _paths;

    public SettingsStore(AppPaths paths)
    {
        _paths = paths;
    }

    public AppSettings Load()
    {
        Directory.CreateDirectory(_paths.DataDirectory);

        if (!File.Exists(_paths.SettingsFile))
        {
            var defaults = AppSettings.CreateDefault();
            Save(defaults);
            return defaults;
        }

        try
        {
            var json = File.ReadAllText(_paths.SettingsFile);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions)
                ?? AppSettings.CreateDefault();

            if (string.IsNullOrWhiteSpace(settings.FullCaptureFolder))
            {
                settings.FullCaptureFolder = AppSettings.CreateDefault().FullCaptureFolder;
            }

            return settings;
        }
        catch (JsonException)
        {
            var backupPath = Path.Combine(
                _paths.DataDirectory,
                $"config.invalid-{DateTime.Now:yyyyMMdd-HHmmss}.json");

            File.Move(_paths.SettingsFile, backupPath, overwrite: true);
            var defaults = AppSettings.CreateDefault();
            Save(defaults);
            return defaults;
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(_paths.DataDirectory);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        var temporaryPath = _paths.SettingsFile + ".tmp";

        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, _paths.SettingsFile, overwrite: true);
    }
}
