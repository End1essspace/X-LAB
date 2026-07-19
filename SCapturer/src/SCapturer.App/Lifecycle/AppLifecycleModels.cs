namespace SCapturer.App.Lifecycle;

internal enum AppInstanceCommand
{
    None,
    ShowConsole,
    HideConsole,
    ToggleConsole,
    CaptureFull,
    CaptureRegion,
    CancelRegion,
    Exit,
}

internal sealed record AppLaunchOptions(
    bool StartHidden,
    AppInstanceCommand PrimaryCommand,
    AppInstanceCommand SecondaryCommand,
    string? ErrorMessage)
{
    public bool IsValid => string.IsNullOrWhiteSpace(ErrorMessage);

    public static AppLaunchOptions Parse(IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 0)
        {
            return new AppLaunchOptions(
                StartHidden: false,
                PrimaryCommand: AppInstanceCommand.None,
                SecondaryCommand: AppInstanceCommand.ShowConsole,
                ErrorMessage: null);
        }

        if (arguments.Count != 1)
        {
            return Invalid("SCapturer accepts one command-line action at a time.");
        }

        return arguments[0].Trim().ToLowerInvariant() switch
        {
            "--background" => new AppLaunchOptions(
                StartHidden: true,
                PrimaryCommand: AppInstanceCommand.None,
                SecondaryCommand: AppInstanceCommand.None,
                ErrorMessage: null),

            "--show" => new AppLaunchOptions(
                StartHidden: false,
                PrimaryCommand: AppInstanceCommand.None,
                SecondaryCommand: AppInstanceCommand.ShowConsole,
                ErrorMessage: null),

            "--hide" => new AppLaunchOptions(
                StartHidden: true,
                PrimaryCommand: AppInstanceCommand.None,
                SecondaryCommand: AppInstanceCommand.HideConsole,
                ErrorMessage: null),

            "--toggle-console" => new AppLaunchOptions(
                StartHidden: false,
                PrimaryCommand: AppInstanceCommand.ToggleConsole,
                SecondaryCommand: AppInstanceCommand.ToggleConsole,
                ErrorMessage: null),

            "--capture-full" => new AppLaunchOptions(
                StartHidden: true,
                PrimaryCommand: AppInstanceCommand.CaptureFull,
                SecondaryCommand: AppInstanceCommand.CaptureFull,
                ErrorMessage: null),

            "--capture-region" => new AppLaunchOptions(
                StartHidden: true,
                PrimaryCommand: AppInstanceCommand.CaptureRegion,
                SecondaryCommand: AppInstanceCommand.CaptureRegion,
                ErrorMessage: null),

            "--cancel-region" => new AppLaunchOptions(
                StartHidden: true,
                PrimaryCommand: AppInstanceCommand.CancelRegion,
                SecondaryCommand: AppInstanceCommand.CancelRegion,
                ErrorMessage: null),

            "--exit" => new AppLaunchOptions(
                StartHidden: true,
                PrimaryCommand: AppInstanceCommand.Exit,
                SecondaryCommand: AppInstanceCommand.Exit,
                ErrorMessage: null),

            _ => Invalid(
                "Unknown argument. Supported: --background, --show, --hide, " +
                "--toggle-console, --capture-full, --capture-region, " +
                "--cancel-region, --exit."),
        };
    }

    private static AppLaunchOptions Invalid(string message)
    {
        return new AppLaunchOptions(
            StartHidden: false,
            PrimaryCommand: AppInstanceCommand.None,
            SecondaryCommand: AppInstanceCommand.None,
            ErrorMessage: message);
    }
}
