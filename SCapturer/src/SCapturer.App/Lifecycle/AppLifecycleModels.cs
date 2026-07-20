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
    int? ResumeAfterProcessId,
    bool WaitForSecondaryExit,
    bool RemoveAutostart,
    string? ErrorMessage)
{
    private const string ResumeBackgroundPrefix = "--resume-background=";

    public bool IsValid => string.IsNullOrWhiteSpace(ErrorMessage);

    public bool IsMaintenanceCommand =>
        WaitForSecondaryExit || RemoveAutostart;

    public static AppLaunchOptions Parse(IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 0)
        {
            return Create(
                startHidden: false,
                primaryCommand: AppInstanceCommand.None,
                secondaryCommand: AppInstanceCommand.ShowConsole);
        }

        if (arguments.Count != 1)
        {
            return Invalid("SCapturer accepts one command-line action at a time.");
        }

        var argument = arguments[0].Trim();
        if (argument.StartsWith(
                ResumeBackgroundPrefix,
                StringComparison.OrdinalIgnoreCase))
        {
            var processIdText = argument[ResumeBackgroundPrefix.Length..];
            if (!int.TryParse(processIdText, out var processId) || processId <= 0)
            {
                return Invalid(
                    "The background-resume process identifier is invalid.");
            }

            return Create(
                startHidden: true,
                primaryCommand: AppInstanceCommand.None,
                secondaryCommand: AppInstanceCommand.None,
                resumeAfterProcessId: processId);
        }

        return argument.ToLowerInvariant() switch
        {
            "--background" => Create(
                startHidden: true,
                primaryCommand: AppInstanceCommand.None,
                secondaryCommand: AppInstanceCommand.None),

            "--show" => Create(
                startHidden: false,
                primaryCommand: AppInstanceCommand.None,
                secondaryCommand: AppInstanceCommand.ShowConsole),

            "--hide" => Create(
                startHidden: true,
                primaryCommand: AppInstanceCommand.None,
                secondaryCommand: AppInstanceCommand.HideConsole),

            "--toggle-console" => Create(
                startHidden: false,
                primaryCommand: AppInstanceCommand.ToggleConsole,
                secondaryCommand: AppInstanceCommand.ToggleConsole),

            "--capture-full" => Create(
                startHidden: true,
                primaryCommand: AppInstanceCommand.CaptureFull,
                secondaryCommand: AppInstanceCommand.CaptureFull),

            "--capture-region" => Create(
                startHidden: true,
                primaryCommand: AppInstanceCommand.CaptureRegion,
                secondaryCommand: AppInstanceCommand.CaptureRegion),

            "--cancel-region" => Create(
                startHidden: true,
                primaryCommand: AppInstanceCommand.CancelRegion,
                secondaryCommand: AppInstanceCommand.CancelRegion),

            "--exit" => Create(
                startHidden: true,
                primaryCommand: AppInstanceCommand.Exit,
                secondaryCommand: AppInstanceCommand.Exit),

            "--shutdown-for-update" => Create(
                startHidden: true,
                primaryCommand: AppInstanceCommand.Exit,
                secondaryCommand: AppInstanceCommand.Exit,
                waitForSecondaryExit: true),

            "--prepare-uninstall" => Create(
                startHidden: true,
                primaryCommand: AppInstanceCommand.Exit,
                secondaryCommand: AppInstanceCommand.Exit,
                waitForSecondaryExit: true,
                removeAutostart: true),

            _ => Invalid(
                "Unknown argument. Supported: --background, --show, --hide, " +
                "--toggle-console, --capture-full, --capture-region, " +
                "--cancel-region, --exit."),
        };
    }

    private static AppLaunchOptions Create(
        bool startHidden,
        AppInstanceCommand primaryCommand,
        AppInstanceCommand secondaryCommand,
        int? resumeAfterProcessId = null,
        bool waitForSecondaryExit = false,
        bool removeAutostart = false)
    {
        return new AppLaunchOptions(
            startHidden,
            primaryCommand,
            secondaryCommand,
            resumeAfterProcessId,
            waitForSecondaryExit,
            removeAutostart,
            ErrorMessage: null);
    }

    private static AppLaunchOptions Invalid(string message)
    {
        return new AppLaunchOptions(
            StartHidden: false,
            PrimaryCommand: AppInstanceCommand.None,
            SecondaryCommand: AppInstanceCommand.None,
            ResumeAfterProcessId: null,
            WaitForSecondaryExit: false,
            RemoveAutostart: false,
            ErrorMessage: message);
    }
}
