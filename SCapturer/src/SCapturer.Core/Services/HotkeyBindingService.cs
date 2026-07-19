using System.Globalization;
using System.Windows.Forms;
using SCapturer.Core.Models;

namespace SCapturer.Core.Services;

public static class HotkeyBindingService
{
    private static readonly IReadOnlyDictionary<string, Keys> KeyAliases =
        new Dictionary<string, Keys>(StringComparer.OrdinalIgnoreCase)
        {
            ["ESC"] = Keys.Escape,
            ["RETURN"] = Keys.Enter,
            ["DEL"] = Keys.Delete,
            ["INS"] = Keys.Insert,
            ["PGUP"] = Keys.PageUp,
            ["PGDN"] = Keys.PageDown,
            ["PRTSC"] = Keys.PrintScreen,
            ["PRTSCREEN"] = Keys.PrintScreen,
            ["PRINTSCREEN"] = Keys.PrintScreen,
            ["SPACEBAR"] = Keys.Space,
            ["ARROWUP"] = Keys.Up,
            ["ARROWDOWN"] = Keys.Down,
            ["ARROWLEFT"] = Keys.Left,
            ["ARROWRIGHT"] = Keys.Right,
        };

    public static HotkeyBindingSet CreateSet(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new HotkeyBindingSet(
            settings.FullCaptureHotkey.CreateSnapshot(),
            settings.RegionCaptureHotkey.CreateSnapshot(),
            settings.ExitHotkey.CreateSnapshot());
    }

    public static bool TryParse(
        string? text,
        out HotkeyBinding binding,
        out string errorMessage)
    {
        binding = new HotkeyBinding();
        errorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(text))
        {
            errorMessage = "The hotkey cannot be empty.";
            return false;
        }

        var parts = text
            .Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 2)
        {
            errorMessage = "Use at least one modifier and one key, for example Ctrl+Shift+G.";
            return false;
        }

        string? keyToken = null;

        foreach (var rawPart in parts)
        {
            var part = rawPart.Trim();

            if (part.Equals("CTRL", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("CONTROL", StringComparison.OrdinalIgnoreCase))
            {
                if (binding.Control)
                {
                    errorMessage = "Ctrl was specified more than once.";
                    return false;
                }

                binding.Control = true;
                continue;
            }

            if (part.Equals("SHIFT", StringComparison.OrdinalIgnoreCase))
            {
                if (binding.Shift)
                {
                    errorMessage = "Shift was specified more than once.";
                    return false;
                }

                binding.Shift = true;
                continue;
            }

            if (part.Equals("ALT", StringComparison.OrdinalIgnoreCase))
            {
                if (binding.Alt)
                {
                    errorMessage = "Alt was specified more than once.";
                    return false;
                }

                binding.Alt = true;
                continue;
            }

            if (part.Equals("WIN", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("WINDOWS", StringComparison.OrdinalIgnoreCase))
            {
                if (binding.Windows)
                {
                    errorMessage = "Win was specified more than once.";
                    return false;
                }

                binding.Windows = true;
                continue;
            }

            if (keyToken is not null)
            {
                errorMessage = "A hotkey can contain only one non-modifier key.";
                return false;
            }

            keyToken = part;
        }

        if (!binding.Control && !binding.Shift && !binding.Alt && !binding.Windows)
        {
            errorMessage = "At least one modifier is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(keyToken))
        {
            errorMessage = "The hotkey does not contain a primary key.";
            return false;
        }

        if (!TryParseKey(keyToken, out var key))
        {
            errorMessage = $"Unsupported key: {keyToken}.";
            return false;
        }

        binding.VirtualKey = (int)key;

        return TryValidate(binding, out errorMessage);
    }

    public static bool TryValidate(
        HotkeyBinding? binding,
        out string errorMessage)
    {
        errorMessage = string.Empty;

        if (binding is null)
        {
            errorMessage = "The hotkey binding is missing.";
            return false;
        }

        if (!binding.Control && !binding.Shift && !binding.Alt && !binding.Windows)
        {
            errorMessage = "At least one modifier is required.";
            return false;
        }

        var key = (Keys)(binding.VirtualKey & (int)Keys.KeyCode);

        if (key == Keys.None ||
            IsModifierKey(key) ||
            !Enum.IsDefined(typeof(Keys), key))
        {
            errorMessage = "The primary key is invalid or is itself a modifier.";
            return false;
        }

        return true;
    }

    public static bool TryValidateSet(
        HotkeyBindingSet bindings,
        out string errorMessage)
    {
        ArgumentNullException.ThrowIfNull(bindings);

        if (!TryValidate(bindings.FullCapture, out errorMessage))
        {
            errorMessage = $"Full capture hotkey: {errorMessage}";
            return false;
        }

        if (!TryValidate(bindings.RegionCapture, out errorMessage))
        {
            errorMessage = $"Region capture hotkey: {errorMessage}";
            return false;
        }

        if (!TryValidate(bindings.Exit, out errorMessage))
        {
            errorMessage = $"Exit hotkey: {errorMessage}";
            return false;
        }

        if (AreEquivalent(bindings.FullCapture, bindings.RegionCapture))
        {
            errorMessage = "Full capture and region capture use the same hotkey.";
            return false;
        }

        if (AreEquivalent(bindings.FullCapture, bindings.Exit))
        {
            errorMessage = "Full capture and exit use the same hotkey.";
            return false;
        }

        if (AreEquivalent(bindings.RegionCapture, bindings.Exit))
        {
            errorMessage = "Region capture and exit use the same hotkey.";
            return false;
        }

        errorMessage = string.Empty;
        return true;
    }

    public static string Format(HotkeyBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);

        var parts = new List<string>(5);

        if (binding.Control)
        {
            parts.Add("Ctrl");
        }

        if (binding.Shift)
        {
            parts.Add("Shift");
        }

        if (binding.Alt)
        {
            parts.Add("Alt");
        }

        if (binding.Windows)
        {
            parts.Add("Win");
        }

        parts.Add(FormatKey((Keys)(binding.VirtualKey & (int)Keys.KeyCode)));
        return string.Join("+", parts);
    }

    public static bool AreEquivalent(
        HotkeyBinding left,
        HotkeyBinding right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        return left.Control == right.Control &&
            left.Shift == right.Shift &&
            left.Alt == right.Alt &&
            left.Windows == right.Windows &&
            (left.VirtualKey & (int)Keys.KeyCode) ==
            (right.VirtualKey & (int)Keys.KeyCode);
    }

    private static bool TryParseKey(string token, out Keys key)
    {
        key = Keys.None;

        if (token.Length == 1)
        {
            var character = char.ToUpperInvariant(token[0]);

            if (character is >= 'A' and <= 'Z')
            {
                key = (Keys)character;
                return true;
            }

            if (character is >= '0' and <= '9')
            {
                key = (Keys)((int)Keys.D0 + (character - '0'));
                return true;
            }
        }

        if (KeyAliases.TryGetValue(token, out key))
        {
            return true;
        }

        if (token.StartsWith("F", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(
                token.AsSpan(1),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var functionNumber) &&
            functionNumber is >= 1 and <= 24)
        {
            key = (Keys)((int)Keys.F1 + (functionNumber - 1));
            return true;
        }

        if (Enum.TryParse<Keys>(token, ignoreCase: true, out var parsed))
        {
            key = parsed & Keys.KeyCode;
            return key != Keys.None;
        }

        return false;
    }

    private static string FormatKey(Keys key)
    {
        if (key is >= Keys.A and <= Keys.Z)
        {
            return key.ToString();
        }

        if (key is >= Keys.D0 and <= Keys.D9)
        {
            return ((int)key - (int)Keys.D0).ToString(CultureInfo.InvariantCulture);
        }

        return key switch
        {
            Keys.Escape => "Esc",
            Keys.PageUp => "PageUp",
            Keys.PageDown => "PageDown",
            Keys.PrintScreen => "PrintScreen",
            _ => key.ToString(),
        };
    }

    private static bool IsModifierKey(Keys key)
    {
        return key is
            Keys.ControlKey or
            Keys.LControlKey or
            Keys.RControlKey or
            Keys.ShiftKey or
            Keys.LShiftKey or
            Keys.RShiftKey or
            Keys.Menu or
            Keys.LMenu or
            Keys.RMenu or
            Keys.LWin or
            Keys.RWin;
    }
}
