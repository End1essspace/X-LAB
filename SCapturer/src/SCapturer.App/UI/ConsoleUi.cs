using SCapturer.Core.Models;
using SCapturer.Core.Services;

namespace SCapturer.App.UI;

internal sealed class ConsoleUi
{
    private readonly AppPaths _paths;

    public ConsoleUi(AppPaths paths)
    {
        _paths = paths;
    }

    public void RenderMainMenu(AppSettings settings, string statusMessage)
    {
        Console.Clear();
        WriteRule("SCAPTURER");
        Console.WriteLine(" Performance-first Windows screenshot utility");
        WriteRule();
        WritePair("Listener", "ACTIVE");
        WritePair("Full capture", "Ctrl + Shift + G");
        WritePair("Exit", "Ctrl + Shift + Q");
        WritePair("Format", "PNG (lossless)");
        WritePair("Clipboard", OnOff(settings.CopyToClipboard));
        WritePair("Sound", OnOff(settings.PlayCaptureSound));
        WritePair("Save folder", settings.FullCaptureFolder);
        WriteRule();
        Console.WriteLine(" [1] Capture full desktop now");
        Console.WriteLine(" [2] Change save folder");
        Console.WriteLine(" [3] Open save folder");
        Console.WriteLine(" [4] Toggle clipboard copy");
        Console.WriteLine(" [5] Toggle capture sound");
        Console.WriteLine(" [0] Exit");
        WriteRule();
        Console.WriteLine($" Status: {statusMessage}");
        Console.WriteLine($" Config: {_paths.SettingsFile}");
        WriteRule();
        Console.Write(" Select an option: ");
    }

    public string? PromptForFolder(string currentFolder)
    {
        Console.Clear();
        WriteRule("CHANGE SAVE FOLDER");
        Console.WriteLine($" Current: {currentFolder}");
        Console.WriteLine();
        Console.WriteLine(" Enter an absolute path. Leave empty to cancel.");
        Console.Write("> ");
        return Console.ReadLine();
    }

    private static void WritePair(string label, string value)
    {
        Console.WriteLine($" {label,-14}: {value}");
    }

    private static void WriteRule(string? title = null)
    {
        const int width = 78;

        if (string.IsNullOrWhiteSpace(title))
        {
            Console.WriteLine(new string('─', width));
            return;
        }

        var content = $" {title} ";
        var remaining = Math.Max(0, width - content.Length);
        var left = remaining / 2;
        var right = remaining - left;
        Console.WriteLine(new string('─', left) + content + new string('─', right));
    }

    private static string OnOff(bool value) => value ? "ON" : "OFF";
}
