using System.Text.Json;
using SCapturer.Core.Models;

namespace SCapturer.Core.Services;

public sealed class SettingsStore
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
            var migrated = TryLoadLegacySettings();
            if (migrated is not null)
            {
                Save(migrated);
                return migrated;
            }

            var defaults = AppSettings.CreateDefault();
            Save(defaults);
            return defaults;
        }

        return LoadFromFile(_paths.SettingsFile, backUpInvalidFile: true);
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        Directory.CreateDirectory(_paths.DataDirectory);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        var temporaryPath = _paths.SettingsFile + ".tmp";

        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, _paths.SettingsFile, overwrite: true);
    }

    private AppSettings? TryLoadLegacySettings()
    {
        if (!File.Exists(_paths.LegacySettingsFile))
        {
            return null;
        }

        try
        {
            return LoadFromFile(_paths.LegacySettingsFile, backUpInvalidFile: false);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private AppSettings LoadFromFile(string filePath, bool backUpInvalidFile)
    {
        try
        {
            var json = File.ReadAllText(filePath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions)
                ?? AppSettings.CreateDefault();

            if (string.IsNullOrWhiteSpace(settings.FullCaptureFolder))
            {
                settings.FullCaptureFolder = AppSettings.CreateDefault().FullCaptureFolder;
            }

            return settings;
        }
        catch (JsonException) when (backUpInvalidFile)
        {
            var backupPath = Path.Combine(
                _paths.DataDirectory,
                $"config.invalid-{DateTime.Now:yyyyMMdd-HHmmss}.json");

            File.Move(filePath, backupPath, overwrite: true);
            var defaults = AppSettings.CreateDefault();
            Save(defaults);
            return defaults;
        }
    }
}
