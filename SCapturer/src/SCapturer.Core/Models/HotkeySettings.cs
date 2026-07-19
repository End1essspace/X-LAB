namespace SCapturer.Core.Models;

public enum HotkeyAction
{
    FullCapture,
    RegionCapture,
    Exit,
    ToggleConsole,
}

public sealed class HotkeyBinding
{
    public bool Control { get; set; }

    public bool Shift { get; set; }

    public bool Alt { get; set; }

    public bool Windows { get; set; }

    public int VirtualKey { get; set; }

    public HotkeyBinding CreateSnapshot()
    {
        return new HotkeyBinding
        {
            Control = Control,
            Shift = Shift,
            Alt = Alt,
            Windows = Windows,
            VirtualKey = VirtualKey,
        };
    }

    public static HotkeyBinding CreateDefaultFullCapture()
    {
        return new HotkeyBinding
        {
            Control = true,
            Shift = true,
            VirtualKey = 'G',
        };
    }

    public static HotkeyBinding CreateDefaultRegionCapture()
    {
        return new HotkeyBinding
        {
            Control = true,
            Shift = true,
            VirtualKey = 'S',
        };
    }

    public static HotkeyBinding CreateDefaultExit()
    {
        return new HotkeyBinding
        {
            Control = true,
            Shift = true,
            VirtualKey = 'Q',
        };
    }

    public static HotkeyBinding CreateDefaultToggleConsole()
    {
        return new HotkeyBinding
        {
            Control = true,
            Shift = true,
            VirtualKey = 'H',
        };
    }
}

public sealed record HotkeyBindingSet(
    HotkeyBinding FullCapture,
    HotkeyBinding RegionCapture,
    HotkeyBinding Exit,
    HotkeyBinding ToggleConsole)
{
    public HotkeyBindingSet CreateSnapshot()
    {
        return new HotkeyBindingSet(
            FullCapture.CreateSnapshot(),
            RegionCapture.CreateSnapshot(),
            Exit.CreateSnapshot(),
            ToggleConsole.CreateSnapshot());
    }
}

public sealed record HotkeyRegistrationResult(
    bool Success,
    string? ErrorMessage)
{
    public static HotkeyRegistrationResult Succeeded { get; } = new(
        Success: true,
        ErrorMessage: null);

    public static HotkeyRegistrationResult Failed(string errorMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        return new HotkeyRegistrationResult(false, errorMessage);
    }
}
