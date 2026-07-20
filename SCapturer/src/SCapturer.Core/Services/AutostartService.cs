using Microsoft.Win32;

namespace SCapturer.Core.Services;

public sealed record AutostartStatus(
    bool IsEnabled,
    bool IsCurrent,
    string ExpectedCommand,
    string? RegisteredCommand,
    string? ErrorMessage);

public sealed class AutostartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SCapturer";

    public AutostartStatus GetStatus()
    {
        var expected = CreateExpectedCommand();

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                RunKeyPath,
                writable: false);
            var registered = key?.GetValue(ValueName) as string;
            var enabled = !string.IsNullOrWhiteSpace(registered);
            var current = enabled && string.Equals(
                registered,
                expected,
                StringComparison.OrdinalIgnoreCase);

            return new AutostartStatus(
                enabled,
                current,
                expected,
                registered,
                ErrorMessage: null);
        }
        catch (Exception exception)
            when (exception is IOException or
                  UnauthorizedAccessException or
                  System.Security.SecurityException)
        {
            return new AutostartStatus(
                IsEnabled: false,
                IsCurrent: false,
                ExpectedCommand: expected,
                RegisteredCommand: null,
                ErrorMessage: exception.Message);
        }
    }

    public AutostartStatus Enable()
    {
        var expected = CreateExpectedCommand();

        using var key = Registry.CurrentUser.CreateSubKey(
            RunKeyPath,
            writable: true)
            ?? throw new InvalidOperationException(
                "Windows did not open the current-user Run registry key.");

        key.SetValue(ValueName, expected, RegistryValueKind.String);
        return GetStatus();
    }

    public AutostartStatus Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            RunKeyPath,
            writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
        return GetStatus();
    }

    private static string CreateExpectedCommand()
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
        {
            throw new InvalidOperationException(
                "SCapturer could not determine its executable path for autostart.");
        }

        return CreateCommand(
            processPath,
            ResolveManagedEntryPath(processPath));
    }

    private static string? ResolveManagedEntryPath(string processPath)
    {
        if (!string.Equals(
                Path.GetFileNameWithoutExtension(processPath),
                "dotnet",
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var candidate = Path.Combine(
            AppContext.BaseDirectory,
            "SCapturer.dll");

        return File.Exists(candidate)
            ? candidate
            : null;
    }

    internal static string CreateCommand(
        string processPath,
        string? entryAssemblyLocation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(processPath);

        var processName = Path.GetFileNameWithoutExtension(processPath);

        if (string.Equals(
                processName,
                "dotnet",
                StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(entryAssemblyLocation))
        {
            return $"{Quote(processPath)} {Quote(entryAssemblyLocation)} --background";
        }

        return $"{Quote(processPath)} --background";
    }

    private static string Quote(string value)
    {
        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }
}
