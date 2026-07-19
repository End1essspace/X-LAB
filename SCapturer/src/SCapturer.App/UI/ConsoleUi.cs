using SCapturer.Core.Models;
using SCapturer.Core.Pipeline;
using SCapturer.Core.Services;

namespace SCapturer.App.UI;

internal sealed class ConsoleUi
{
    private const int MinimumUsefulWidth = 64;
    private const int MinimumUsefulHeight = 22;

    private readonly AppPaths _paths;
    private readonly Dictionary<ConsolePage, int> _selectionByPage = new();

    private ConsolePage _currentPage = ConsolePage.Dashboard;
    private string[] _lastRenderedLines = Array.Empty<string>();
    private int _lastWindowWidth;
    private int _lastWindowHeight;
    private bool _forceFullRedraw = true;

    public ConsoleUi(AppPaths paths)
    {
        _paths = paths;
    }

    public ConsolePage CurrentPage => _currentPage;

    public bool HasWindowSizeChanged()
    {
        return SafeWindowWidth() != _lastWindowWidth ||
            SafeWindowHeight() != _lastWindowHeight;
    }

    public void Navigate(ConsolePage page)
    {
        _currentPage = page;
        _forceFullRedraw = true;
    }

    public ConsoleCommand HandleKey(
        ConsoleKeyInfo key,
        ConsoleViewModel model)
    {
        var entries = BuildMenuEntries(model);
        ClampSelection(entries);

        if (key.Key is ConsoleKey.UpArrow or ConsoleKey.K)
        {
            MoveSelection(entries, -1);
            return ConsoleCommand.Redraw;
        }

        if (key.Key is ConsoleKey.DownArrow or ConsoleKey.J)
        {
            MoveSelection(entries, 1);
            return ConsoleCommand.Redraw;
        }

        if (key.Key == ConsoleKey.Home)
        {
            SetSelection(entries, 0);
            return ConsoleCommand.Redraw;
        }

        if (key.Key == ConsoleKey.End)
        {
            SetSelection(entries, entries.Count - 1);
            return ConsoleCommand.Redraw;
        }

        if (key.Key is ConsoleKey.Escape or ConsoleKey.Backspace)
        {
            return _currentPage == ConsolePage.Dashboard
                ? ConsoleCommand.None
                : new ConsoleCommand(ConsoleAction.Back);
        }

        if (_currentPage == ConsolePage.RecentCaptures)
        {
            if (key.Key == ConsoleKey.R)
            {
                return new ConsoleCommand(ConsoleAction.RefreshRecentCaptures);
            }

            if (key.Key == ConsoleKey.F)
            {
                var recentIndex = GetSelectedRecentIndex(entries);
                return recentIndex >= 0
                    ? new ConsoleCommand(
                        ConsoleAction.OpenRecentCaptureFolder,
                        recentIndex)
                    : ConsoleCommand.None;
            }
        }

        if (key.Key == ConsoleKey.Enter)
        {
            return GetSelectedCommand(entries);
        }

        var digit = key.KeyChar is >= '1' and <= '9'
            ? key.KeyChar - '1'
            : -1;

        if (digit >= 0 && digit < entries.Count)
        {
            SetSelection(entries, digit);
            return entries[digit].Command;
        }

        return ConsoleCommand.None;
    }

    public void Render(ConsoleViewModel model)
    {
        var lines = BuildFrame(model);
        RenderDiff(lines);
    }

    public string? PromptForText(
        string title,
        string currentValue,
        string instructions)
    {
        PreparePrompt();

        try
        {
            Console.WriteLine(title);
            Console.WriteLine(new string('─', Math.Min(78, Math.Max(20, SafeWindowWidth() - 1))));
            Console.WriteLine($"Current: {currentValue}");
            Console.WriteLine();
            Console.WriteLine(instructions);
            Console.WriteLine("Leave empty to cancel.");
            Console.WriteLine();
            Console.Write("> ");
            return Console.ReadLine();
        }
        finally
        {
            TrySetCursorVisible(false);
            _forceFullRedraw = true;
            _lastRenderedLines = Array.Empty<string>();
        }
    }

    public void PrepareForExit()
    {
        try
        {
            Console.ResetColor();
            Console.CursorVisible = true;
            Console.WriteLine();
        }
        catch (IOException)
        {
            // The console may already be detached during process teardown.
        }
    }

    private IReadOnlyList<string> BuildFrame(ConsoleViewModel model)
    {
        var width = SafeWindowWidth();
        var height = SafeWindowHeight();

        if (width < MinimumUsefulWidth || height < MinimumUsefulHeight)
        {
            return
            [
                "SCapturer",
                string.Empty,
                $"Console window is too small ({width}×{height}).",
                $"Resize it to at least {MinimumUsefulWidth}×{MinimumUsefulHeight}.",
                string.Empty,
                $"Status: {model.StatusMessage}",
            ];
        }

        var lines = new List<string>(48);
        var title = PageTitle(_currentPage);

        lines.Add(CenterTitle($"SCAPTURER · {title}", width));
        lines.Add(Rule(width));

        AddHeader(lines, model);
        lines.Add(Rule(width));

        AddPageContent(lines, model, width);

        lines.Add(Rule(width));
        AddFooter(lines, model, width);

        return lines;
    }

    private void AddHeader(
        ICollection<string> lines,
        ConsoleViewModel model)
    {
        lines.Add(
            $" Listener  ACTIVE   Pipeline  {FormatPipeline(model.Pipeline),-28} " +
            $"Benchmark  {(model.BenchmarkInProgress ? "RUNNING" : "IDLE")}");

        lines.Add(
            $" Displays  {model.Topology.MonitorCount} · {model.Topology.DpiMode} · " +
            $"{(model.Topology.IsRemoteSession ? "REMOTE" : "LOCAL")}   " +
            $"Desktop  {model.Topology.VirtualBounds.Width}×{model.Topology.VirtualBounds.Height} " +
            $"@ ({model.Topology.VirtualBounds.X},{model.Topology.VirtualBounds.Y}) · " +
            $"topology v{model.Topology.Version}");
    }

    private void AddPageContent(
        ICollection<string> lines,
        ConsoleViewModel model,
        int width)
    {
        switch (_currentPage)
        {
            case ConsolePage.Dashboard:
                AddDashboard(lines, model);
                break;
            case ConsolePage.CaptureSettings:
                AddCaptureSettings(lines, model);
                break;
            case ConsolePage.Hotkeys:
                AddHotkeys(lines, model);
                break;
            case ConsolePage.SaveLocations:
                AddSaveLocations(lines, model, width);
                break;
            case ConsolePage.Diagnostics:
                AddDiagnostics(lines, model, width);
                break;
            case ConsolePage.RecentCaptures:
                AddRecentCaptures(lines, model, width);
                break;
            case ConsolePage.About:
                AddAbout(lines, model);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private void AddDashboard(
        ICollection<string> lines,
        ConsoleViewModel model)
    {
        lines.Add($" Full capture   {HotkeyBindingService.Format(model.Settings.FullCaptureHotkey)}");
        lines.Add($" Region capture {HotkeyBindingService.Format(model.Settings.RegionCaptureHotkey)}");
        lines.Add($" Exit           {HotkeyBindingService.Format(model.Settings.ExitHotkey)}");
        lines.Add(string.Empty);

        if (model.LastCapture is null)
        {
            lines.Add(" Last capture   No capture has completed in this session.");
        }
        else
        {
            var last = model.LastCapture;
            var kind = last.Kind == CaptureKind.Region ? "REGION" : "FULL";
            lines.Add(
                $" Last capture   {kind} · {last.Width}×{last.Height} · " +
                $"{FormatBytes(last.FileSizeBytes)} · {last.Metrics.TotalMilliseconds:0.0} ms");
            lines.Add($"                {last.FilePath}");
        }

        lines.Add(string.Empty);
        AddMenu(lines, model);
    }

    private void AddCaptureSettings(
        ICollection<string> lines,
        ConsoleViewModel model)
    {
        lines.Add($" Clipboard copy  {OnOff(model.Settings.CopyToClipboard)}");
        lines.Add($" Capture sound   {OnOff(model.Settings.PlayCaptureSound)}");
        lines.Add(" Image format    PNG · lossless · original physical pixels");
        lines.Add(string.Empty);
        AddMenu(lines, model);
    }

    private void AddHotkeys(
        ICollection<string> lines,
        ConsoleViewModel model)
    {
        lines.Add(
            $" Full capture    {HotkeyBindingService.Format(model.Settings.FullCaptureHotkey)}");
        lines.Add(
            $" Region capture  {HotkeyBindingService.Format(model.Settings.RegionCaptureHotkey)}");
        lines.Add(
            $" Exit            {HotkeyBindingService.Format(model.Settings.ExitHotkey)}");
        lines.Add(string.Empty);
        lines.Add(" Enter combinations as text, for example Ctrl+Alt+G or Win+Shift+S.");
        lines.Add(" SCapturer validates duplicates and asks Windows to reserve the new binding.");
        lines.Add(string.Empty);
        AddMenu(lines, model);
    }

    private void AddSaveLocations(
        ICollection<string> lines,
        ConsoleViewModel model,
        int width)
    {
        lines.Add(" Full captures");
        lines.Add("   " + Truncate(model.Settings.FullCaptureFolder, width - 4));
        lines.Add(" Region captures");
        lines.Add("   " + Truncate(model.Settings.SnipCaptureFolder, width - 4));
        lines.Add(string.Empty);
        AddMenu(lines, model);
    }

    private void AddDiagnostics(
        ICollection<string> lines,
        ConsoleViewModel model,
        int width)
    {
        lines.Add($" Capture diagnostics  {OnOff(model.Settings.EnableDiagnostics)}");
        lines.Add(" Metrics");
        lines.Add("   " + Truncate(_paths.CaptureMetricsFile, width - 4));
        lines.Add(" Benchmark reports");
        lines.Add("   " + Truncate(_paths.BenchmarkReportsDirectory, width - 4));
        lines.Add(string.Empty);
        AddMenu(lines, model);
    }

    private void AddRecentCaptures(
        ICollection<string> lines,
        ConsoleViewModel model,
        int width)
    {
        if (model.RecentCaptures.Count == 0)
        {
            lines.Add(" No PNG captures were found in the configured folders.");
            lines.Add(string.Empty);
        }

        AddMenu(lines, model, width);
        lines.Add(string.Empty);
        lines.Add(" Enter opens the selected file · F opens its folder · R refreshes");
    }

    private void AddAbout(
        ICollection<string> lines,
        ConsoleViewModel model)
    {
        lines.Add(" SCapturer");
        lines.Add(" Performance-first Windows screenshot utility.");
        lines.Add(string.Empty);
        lines.Add(" Runtime     .NET 8 · Windows 10 2004+ / Windows 11");
        lines.Add(" Capture     one bounded STA worker · PNG lossless");
        lines.Add(" Geometry    Per-Monitor V2 · physical virtual-desktop coordinates");
        lines.Add(" Interface   interactive console UI with differential rendering");
        lines.Add(string.Empty);
        lines.Add(" Part of X-LAB.");
        lines.Add(string.Empty);
        AddMenu(lines, model);
    }

    private void AddMenu(
        ICollection<string> lines,
        ConsoleViewModel model,
        int availableWidth = 0)
    {
        var entries = BuildMenuEntries(model);
        ClampSelection(entries);
        var selected = GetSelection();

        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            var marker = index == selected ? ">" : " ";
            var number = index < 9 ? $"{index + 1}. " : "   ";
            var text = $"{marker} {number}{entry.Label}";

            if (availableWidth > 0)
            {
                text = Truncate(text, availableWidth);
            }

            lines.Add(text);
        }
    }

    private void AddFooter(
        ICollection<string> lines,
        ConsoleViewModel model,
        int width)
    {
        foreach (var statusLine in Wrap(
                     " Status: " + model.StatusMessage,
                     Math.Max(20, width - 1),
                     maximumLines: 2))
        {
            lines.Add(statusLine);
        }

        lines.Add(
            _currentPage == ConsolePage.Dashboard
                ? " ↑/↓ select · Enter activate · 1-9 shortcuts"
                : " ↑/↓ select · Enter activate · Esc back · 1-9 shortcuts");
    }

    private IReadOnlyList<MenuEntry> BuildMenuEntries(ConsoleViewModel model)
    {
        return _currentPage switch
        {
            ConsolePage.Dashboard =>
            [
                Entry("Capture full desktop", ConsoleAction.CaptureFull),
                Entry("Capture selected region", ConsoleAction.CaptureRegion),
                Entry("Capture settings", ConsoleAction.OpenCaptureSettings),
                Entry("Hotkeys", ConsoleAction.OpenHotkeys),
                Entry("Save locations", ConsoleAction.OpenSaveLocations),
                Entry("Diagnostics and benchmark", ConsoleAction.OpenDiagnostics),
                Entry("Recent captures", ConsoleAction.OpenRecentCaptures),
                Entry("About", ConsoleAction.OpenAbout),
                Entry("Exit SCapturer", ConsoleAction.Exit),
            ],

            ConsolePage.CaptureSettings =>
            [
                Entry(
                    $"Toggle clipboard copy · currently {OnOff(model.Settings.CopyToClipboard)}",
                    ConsoleAction.ToggleClipboard),
                Entry(
                    $"Toggle capture sound · currently {OnOff(model.Settings.PlayCaptureSound)}",
                    ConsoleAction.ToggleSound),
                Entry("Back to dashboard", ConsoleAction.Back),
            ],

            ConsolePage.Hotkeys =>
            [
                Entry(
                    $"Edit full capture · {HotkeyBindingService.Format(model.Settings.FullCaptureHotkey)}",
                    ConsoleAction.EditFullHotkey),
                Entry(
                    $"Edit region capture · {HotkeyBindingService.Format(model.Settings.RegionCaptureHotkey)}",
                    ConsoleAction.EditRegionHotkey),
                Entry(
                    $"Edit exit · {HotkeyBindingService.Format(model.Settings.ExitHotkey)}",
                    ConsoleAction.EditExitHotkey),
                Entry("Restore default hotkeys", ConsoleAction.RestoreDefaultHotkeys),
                Entry("Back to dashboard", ConsoleAction.Back),
            ],

            ConsolePage.SaveLocations =>
            [
                Entry("Change full capture folder", ConsoleAction.ChangeFullFolder),
                Entry("Change region capture folder", ConsoleAction.ChangeSnipFolder),
                Entry("Open full capture folder", ConsoleAction.OpenFullFolder),
                Entry("Open region capture folder", ConsoleAction.OpenSnipFolder),
                Entry("Back to dashboard", ConsoleAction.Back),
            ],

            ConsolePage.Diagnostics =>
            [
                Entry(
                    $"Toggle diagnostics · currently {OnOff(model.Settings.EnableDiagnostics)}",
                    ConsoleAction.ToggleDiagnostics),
                Entry(
                    model.BenchmarkInProgress
                        ? "Baseline benchmark is running"
                        : "Run full-capture baseline benchmark",
                    model.BenchmarkInProgress
                        ? ConsoleAction.None
                        : ConsoleAction.RunBenchmark),
                Entry("Open diagnostics folder", ConsoleAction.OpenDiagnosticsFolder),
                Entry("Back to dashboard", ConsoleAction.Back),
            ],

            ConsolePage.RecentCaptures => BuildRecentEntries(model),

            ConsolePage.About =>
            [
                Entry("Back to dashboard", ConsoleAction.Back),
            ],

            _ => throw new ArgumentOutOfRangeException(),
        };
    }

    private static IReadOnlyList<MenuEntry> BuildRecentEntries(
        ConsoleViewModel model)
    {
        var entries = new List<MenuEntry>(model.RecentCaptures.Count + 2);

        for (var index = 0; index < model.RecentCaptures.Count; index++)
        {
            var item = model.RecentCaptures[index];
            var kind = item.Kind == CaptureKind.Region ? "REGION" : "FULL";
            entries.Add(new MenuEntry(
                $"{item.LastWriteTime.ToLocalTime():yyyy-MM-dd HH:mm:ss} · " +
                $"{kind,-6} · {FormatBytes(item.FileSizeBytes),8} · {item.FileName}",
                new ConsoleCommand(ConsoleAction.OpenRecentCapture, index),
                RecentIndex: index));
        }

        entries.Add(Entry("Refresh recent captures", ConsoleAction.RefreshRecentCaptures));
        entries.Add(Entry("Back to dashboard", ConsoleAction.Back));
        return entries;
    }

    private ConsoleCommand GetSelectedCommand(IReadOnlyList<MenuEntry> entries)
    {
        if (entries.Count == 0)
        {
            return ConsoleCommand.None;
        }

        ClampSelection(entries);
        return entries[GetSelection()].Command;
    }

    private int GetSelectedRecentIndex(IReadOnlyList<MenuEntry> entries)
    {
        if (entries.Count == 0)
        {
            return -1;
        }

        ClampSelection(entries);
        return entries[GetSelection()].RecentIndex;
    }

    private void MoveSelection(
        IReadOnlyList<MenuEntry> entries,
        int delta)
    {
        if (entries.Count == 0)
        {
            return;
        }

        var current = GetSelection();
        var next = (current + delta) % entries.Count;
        if (next < 0)
        {
            next += entries.Count;
        }

        _selectionByPage[_currentPage] = next;
    }

    private void SetSelection(
        IReadOnlyList<MenuEntry> entries,
        int selection)
    {
        if (entries.Count == 0)
        {
            _selectionByPage[_currentPage] = 0;
            return;
        }

        _selectionByPage[_currentPage] = Math.Clamp(
            selection,
            0,
            entries.Count - 1);
    }

    private void ClampSelection(IReadOnlyList<MenuEntry> entries)
    {
        SetSelection(entries, GetSelection());
    }

    private int GetSelection()
    {
        return _selectionByPage.TryGetValue(_currentPage, out var selection)
            ? selection
            : 0;
    }

    private void RenderDiff(IReadOnlyList<string> sourceLines)
    {
        var width = SafeWindowWidth();
        var height = SafeWindowHeight();
        var contentWidth = Math.Max(1, width - 1);

        var normalized = sourceLines
            .Take(height)
            .Select(line => FitLine(line, contentWidth))
            .ToArray();

        var sizeChanged =
            width != _lastWindowWidth ||
            height != _lastWindowHeight;

        try
        {
            TrySetCursorVisible(false);

            if (_forceFullRedraw || sizeChanged)
            {
                Console.Clear();
                _lastRenderedLines = Array.Empty<string>();
                _forceFullRedraw = false;
            }

            var maximumLines = Math.Max(
                normalized.Length,
                _lastRenderedLines.Length);

            for (var index = 0; index < maximumLines && index < height; index++)
            {
                var next = index < normalized.Length
                    ? normalized[index]
                    : new string(' ', contentWidth);

                var previous = index < _lastRenderedLines.Length
                    ? _lastRenderedLines[index]
                    : null;

                if (string.Equals(next, previous, StringComparison.Ordinal))
                {
                    continue;
                }

                Console.SetCursorPosition(0, index);
                Console.Write(next);
            }

            _lastRenderedLines = normalized;
            _lastWindowWidth = width;
            _lastWindowHeight = height;

            var cursorLine = Math.Min(
                Math.Max(0, normalized.Length - 1),
                Math.Max(0, height - 1));

            Console.SetCursorPosition(0, cursorLine);
        }
        catch (Exception exception)
            when (exception is IOException or
                  ArgumentOutOfRangeException or
                  InvalidOperationException)
        {
            FallbackRender(normalized);
        }
    }

    private void FallbackRender(IReadOnlyList<string> lines)
    {
        try
        {
            Console.Clear();
            foreach (var line in lines)
            {
                Console.WriteLine(line.TrimEnd());
            }
        }
        catch (IOException)
        {
            // A detached console cannot be rendered.
        }

        _lastRenderedLines = lines.ToArray();
        _lastWindowWidth = SafeWindowWidth();
        _lastWindowHeight = SafeWindowHeight();
        _forceFullRedraw = false;
    }

    private void PreparePrompt()
    {
        try
        {
            Console.Clear();
            Console.CursorVisible = true;
        }
        catch (IOException)
        {
            // ReadLine may still work in hosts that do not support cursor control.
        }
    }

    private static MenuEntry Entry(
        string label,
        ConsoleAction action)
    {
        return new MenuEntry(
            label,
            new ConsoleCommand(action),
            RecentIndex: -1);
    }

    private static string PageTitle(ConsolePage page)
    {
        return page switch
        {
            ConsolePage.Dashboard => "DASHBOARD",
            ConsolePage.CaptureSettings => "CAPTURE SETTINGS",
            ConsolePage.Hotkeys => "HOTKEYS",
            ConsolePage.SaveLocations => "SAVE LOCATIONS",
            ConsolePage.Diagnostics => "DIAGNOSTICS",
            ConsolePage.RecentCaptures => "RECENT CAPTURES",
            ConsolePage.About => "ABOUT",
            _ => page.ToString().ToUpperInvariant(),
        };
    }

    private static string FormatPipeline(CapturePipelineSnapshot pipeline)
    {
        var state = pipeline.State.ToString().ToUpperInvariant();
        var active = pipeline.ActiveKind is null
            ? string.Empty
            : " " + FormatKind(pipeline.ActiveKind.Value);

        if (!pipeline.HasPendingRequest)
        {
            return state + active;
        }

        var pending = pipeline.PendingKind is null
            ? string.Empty
            : " " + FormatKind(pipeline.PendingKind.Value);

        return $"{state}{active} + 1 PENDING{pending}";
    }

    private static string FormatKind(CaptureKind kind)
    {
        return kind == CaptureKind.Region ? "REGION" : "FULL";
    }

    private static string OnOff(bool value) => value ? "ON" : "OFF";

    private static string Rule(int width)
    {
        return new string('─', Math.Max(1, width - 1));
    }

    private static string CenterTitle(string text, int width)
    {
        var available = Math.Max(1, width - 1);
        if (text.Length >= available)
        {
            return Truncate(text, available);
        }

        return new string(' ', (available - text.Length) / 2) + text;
    }

    private static string FitLine(string value, int width)
    {
        var truncated = Truncate(value, width);
        return truncated.PadRight(width);
    }

    private static string Truncate(string value, int width)
    {
        if (width <= 0)
        {
            return string.Empty;
        }

        if (value.Length <= width)
        {
            return value;
        }

        return width == 1
            ? "…"
            : value[..(width - 1)] + "…";
    }

    private static IEnumerable<string> Wrap(
        string value,
        int width,
        int maximumLines)
    {
        if (string.IsNullOrEmpty(value))
        {
            yield return string.Empty;
            yield break;
        }

        var remaining = value;

        for (var lineIndex = 0;
             lineIndex < maximumLines && remaining.Length > 0;
             lineIndex++)
        {
            if (remaining.Length <= width)
            {
                yield return remaining;
                yield break;
            }

            var split = remaining.LastIndexOf(' ', width - 1);
            if (split <= 0)
            {
                split = width;
            }

            var line = remaining[..split].TrimEnd();
            remaining = remaining[split..].TrimStart();

            if (lineIndex == maximumLines - 1 && remaining.Length > 0)
            {
                yield return Truncate(line + " " + remaining, width);
                yield break;
            }

            yield return line;
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double value = bytes;
        var unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.##} {units[unitIndex]}";
    }

    private static int SafeWindowWidth()
    {
        try
        {
            return Math.Max(1, Console.WindowWidth);
        }
        catch (IOException)
        {
            return 100;
        }
    }

    private static int SafeWindowHeight()
    {
        try
        {
            return Math.Max(1, Console.WindowHeight);
        }
        catch (IOException)
        {
            return 30;
        }
    }

    private static void TrySetCursorVisible(bool visible)
    {
        try
        {
            Console.CursorVisible = visible;
        }
        catch (IOException)
        {
            // Some terminal hosts do not expose cursor visibility.
        }
    }

    private sealed record MenuEntry(
        string Label,
        ConsoleCommand Command,
        int RecentIndex);
}
