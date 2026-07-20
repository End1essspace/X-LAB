using SCapturer.Core.Capture;
using SCapturer.Core.Models;
using SCapturer.Core.Pipeline;
using SCapturer.Core.Services;

namespace SCapturer.App.UI;

internal sealed class ConsoleUi
{
    private const int MinimumUsefulWidth = 64;
    private const int MinimumUsefulHeight = 28;

    private readonly AppPaths _paths;
    private readonly Dictionary<ConsolePage, int> _selectionByPage = new();

    private ConsolePage _currentPage = ConsolePage.Dashboard;
    private StyledConsoleLine[] _lastRenderedLines = Array.Empty<StyledConsoleLine>();
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

    public void Invalidate()
    {
        _forceFullRedraw = true;
        _lastRenderedLines = Array.Empty<StyledConsoleLine>();
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

        var digit = key.KeyChar switch
        {
            >= '1' and <= '9' => key.KeyChar - '1',
            '0' => 9,
            _ => -1,
        };

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
            _lastRenderedLines = Array.Empty<StyledConsoleLine>();
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
        catch (Exception exception)
            when (exception is IOException or InvalidOperationException)
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
            $" Listener  ACTIVE   Console  {(model.ConsoleVisible ? "VISIBLE" : "HIDDEN"),-7} " +
            $"Pipeline  {FormatPipeline(model.Pipeline),-24} " +
            $"Benchmark  {(model.BenchmarkInProgress ? "RUNNING" : "IDLE")}");

        var backendFallback = model.BackendSelection.IsFallback ? " · FALLBACK" : string.Empty;
        lines.Add(
            $" Backend   {model.BackendSelection.ActiveName} · " +
            $"mode {FormatBackendMode(model.Settings.CaptureBackend)}{backendFallback}");

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
            case ConsolePage.Background:
                AddBackground(lines, model, width);
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
        lines.Add($" Console        {HotkeyBindingService.Format(model.Settings.ToggleConsoleHotkey)}");
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
                $"{FormatBytes(last.FileSizeBytes)} · {last.Metrics.TotalMilliseconds:0.0} ms · " +
                last.BackendName);
            lines.Add($"                {last.FilePath}");

            if (last.Warnings is { Count: > 0 })
            {
                lines.Add(
                    $" Capture note  {last.Warnings.Count} warning(s); see Status or diagnostics.");
            }
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
        lines.Add($" Backend mode    {FormatBackendMode(model.Settings.CaptureBackend)}");
        lines.Add($" Active backend  {model.BackendSelection.ActiveName}");
        if (model.BackendSelection.IsFallback)
        {
            lines.Add(" Fallback reason " + (model.BackendSelection.FallbackReason ?? "Unavailable native backend"));
        }
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
        lines.Add(
            $" Toggle console  {HotkeyBindingService.Format(model.Settings.ToggleConsoleHotkey)}");
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
        lines.Add(" Comparison gate  native needs ≥20% p95 or allocation improvement without >5% median regression");
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

    private void AddBackground(
        ICollection<string> lines,
        ConsoleViewModel model,
        int width)
    {
        var startupState = model.Autostart.ErrorMessage is not null
            ? "ERROR"
            : model.Autostart.IsEnabled
                ? model.Autostart.IsCurrent ? "ENABLED" : "STALE"
                : "DISABLED";

        lines.Add($" Console state   {(model.ConsoleVisible ? "VISIBLE" : "HIDDEN")}");
        lines.Add($" Launch mode     {(model.StartedInBackground ? "BACKGROUND" : "INTERACTIVE")}");
        lines.Add($" Console hotkey  {HotkeyBindingService.Format(model.Settings.ToggleConsoleHotkey)}");
        lines.Add(" Close button    BACKGROUND HANDOFF");
        lines.Add($" Windows startup {startupState}");
        lines.Add(" Startup command");
        lines.Add("   " + Truncate(model.Autostart.ExpectedCommand, width - 4));

        if (!string.IsNullOrWhiteSpace(model.Autostart.ErrorMessage))
        {
            lines.Add(" Startup error   " + Truncate(model.Autostart.ErrorMessage!, width - 18));
        }
        else if (model.Autostart.IsEnabled && !model.Autostart.IsCurrent)
        {
            lines.Add(" Startup note    Existing registration points to another build or path.");
        }

        lines.Add(string.Empty);
        AddMenu(lines, model);
    }

    private void AddAbout(
        ICollection<string> lines,
        ConsoleViewModel model)
    {
        lines.Add(" SCapturer");
        lines.Add(" Performance-first Windows screenshot utility.");
        lines.Add(string.Empty);
        lines.Add(" Runtime     .NET 8 · Windows 10 2004+ / Windows 11");
        lines.Add(" Capture     reference GDI+ and native GDI + WIC backends");
        lines.Add(" Storage     atomic PNG commit · independent clipboard dispatcher");
        lines.Add(" Geometry    Per-Monitor V2 · physical virtual-desktop coordinates");
        lines.Add(" Interface   interactive console UI with differential rendering");
        lines.Add(" Lifecycle   background mode · single-instance IPC · user autostart");
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
            var number = index switch
            {
                < 9 => $"{index + 1}. ",
                9 => "0. ",
                _ => "   ",
            };
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
                ? " ↑/↓ select · Enter activate · 1-9/0 shortcuts"
                : " ↑/↓ select · Enter activate · Esc back · 1-9/0 shortcuts");
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
                Entry("Background and startup", ConsoleAction.OpenBackground),
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
                Entry(
                    $"Cycle capture backend · {FormatBackendMode(model.Settings.CaptureBackend)}",
                    ConsoleAction.CycleCaptureBackend),
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
                Entry(
                    $"Edit toggle console · {HotkeyBindingService.Format(model.Settings.ToggleConsoleHotkey)}",
                    ConsoleAction.EditToggleConsoleHotkey),
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
                        ? "A benchmark is running"
                        : "Run selected-backend baseline benchmark",
                    model.BenchmarkInProgress
                        ? ConsoleAction.None
                        : ConsoleAction.RunBenchmark),
                Entry(
                    model.BenchmarkInProgress
                        ? "Backend comparison unavailable while benchmark runs"
                        : "Compare Reference GDI+ vs Native GDI + WIC",
                    model.BenchmarkInProgress
                        ? ConsoleAction.None
                        : ConsoleAction.RunBackendComparison),
                Entry("Open diagnostics folder", ConsoleAction.OpenDiagnosticsFolder),
                Entry("Back to dashboard", ConsoleAction.Back),
            ],

            ConsolePage.RecentCaptures => BuildRecentEntries(model),

            ConsolePage.Background =>
            [
                Entry(
                    model.Autostart.IsEnabled && model.Autostart.IsCurrent
                        ? "Disable Windows autostart"
                        : model.Autostart.IsEnabled
                            ? "Repair Windows autostart registration"
                            : "Enable Windows autostart",
                    ConsoleAction.ToggleAutostart),
                Entry("Hide console and continue in background", ConsoleAction.HideConsole),
                Entry("Back to dashboard", ConsoleAction.Back),
            ],

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
            .Select((line, index) => StyleLine(line, index))
            .ToArray();

        var sizeChanged =
            width != _lastWindowWidth ||
            height != _lastWindowHeight;

        try
        {
            TrySetCursorVisible(false);

            if (_forceFullRedraw || sizeChanged)
            {
                Console.ResetColor();
                Console.Clear();
                _lastRenderedLines = Array.Empty<StyledConsoleLine>();
                _forceFullRedraw = false;
            }

            var maximumLines = Math.Max(
                normalized.Length,
                _lastRenderedLines.Length);

            for (var index = 0; index < maximumLines && index < height; index++)
            {
                var next = index < normalized.Length
                    ? normalized[index]
                    : StyledConsoleLine.CreatePlain(
                        new string(' ', contentWidth),
                        ConsoleColor.Gray,
                        ConsoleColor.Black);

                var previous = index < _lastRenderedLines.Length
                    ? _lastRenderedLines[index]
                    : null;

                if (previous is not null &&
                    string.Equals(
                        next.Signature,
                        previous.Signature,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                Console.SetCursorPosition(0, index);
                WriteStyledLine(next);
            }

            Console.ResetColor();
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

    private void FallbackRender(IReadOnlyList<StyledConsoleLine> lines)
    {
        try
        {
            Console.ResetColor();
            Console.Clear();
            foreach (var line in lines)
            {
                WriteStyledLine(line);
                Console.ResetColor();
                Console.WriteLine();
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

    private static StyledConsoleLine StyleLine(string text, int lineIndex)
    {
        var foreground = Enumerable.Repeat(
            ConsoleColor.Gray,
            text.Length).ToArray();
        var background = Enumerable.Repeat(
            ConsoleColor.Black,
            text.Length).ToArray();
        var trimmed = text.Trim();

        if (lineIndex == 0)
        {
            ApplyStyle(
                foreground,
                background,
                0,
                text.Length,
                ConsoleColor.Cyan,
                ConsoleColor.Black);
        }
        else if (trimmed.Length > 0 && trimmed.All(character => character == '─'))
        {
            ApplyStyle(
                foreground,
                background,
                0,
                text.Length,
                ConsoleColor.DarkGray,
                ConsoleColor.Black);
        }
        else if (text.TrimStart().StartsWith("> ", StringComparison.Ordinal))
        {
            ApplyStyle(
                foreground,
                background,
                0,
                text.Length,
                ConsoleColor.White,
                ConsoleColor.DarkBlue);
        }
        else
        {
            if (IsNavigationLine(trimmed))
            {
                ApplyStyle(
                    foreground,
                    background,
                    0,
                    text.Length,
                    ConsoleColor.DarkGray,
                    ConsoleColor.Black);
            }
            else if (IsSectionHeading(trimmed))
            {
                ApplyStyle(
                    foreground,
                    background,
                    0,
                    text.Length,
                    ConsoleColor.Cyan,
                    ConsoleColor.Black);
            }
            else if (LooksLikePath(trimmed))
            {
                ApplyStyle(
                    foreground,
                    background,
                    0,
                    text.Length,
                    ConsoleColor.DarkGray,
                    ConsoleColor.Black);
            }

            ApplyPrefixStyle(
                text,
                foreground,
                background,
                " Status:",
                ConsoleColor.DarkGray);

            ApplySemanticWords(text, foreground, background);
            ApplyHotkeyStyles(text, foreground, background);
        }

        return StyledConsoleLine.Create(
            text,
            foreground,
            background);
    }

    private static void ApplySemanticWords(
        string text,
        ConsoleColor[] foreground,
        ConsoleColor[] background)
    {
        ApplyPhraseStyle(
            text,
            foreground,
            background,
            "Native GDI + WIC",
            ConsoleColor.Cyan);
        ApplyPhraseStyle(
            text,
            foreground,
            background,
            "Reference GDI+",
            ConsoleColor.DarkCyan);

        foreach (var word in new[]
                 {
                     "ACTIVE", "VISIBLE", "ENABLED", "PASS", "COMPLETED",
                     "SAVED", "ON",
                 })
        {
            ApplyWordStyle(
                text,
                foreground,
                background,
                word,
                ConsoleColor.Green);
        }

        foreach (var word in new[]
                 {
                     "RUNNING", "CAPTURING", "SAVING", "PUBLISHING",
                     "QUEUED", "AUTO", "BACKGROUND", "HANDOFF",
                 })
        {
            ApplyWordStyle(
                text,
                foreground,
                background,
                word,
                ConsoleColor.Cyan);
        }

        foreach (var word in new[]
                 {
                     "WARNING", "WARN", "FALLBACK", "STALE", "COALESCED",
                 })
        {
            ApplyWordStyle(
                text,
                foreground,
                background,
                word,
                ConsoleColor.Yellow);
        }

        foreach (var word in new[]
                 {
                     "ERROR", "FAILED", "FAIL", "REJECTED",
                 })
        {
            ApplyWordStyle(
                text,
                foreground,
                background,
                word,
                ConsoleColor.Red);
        }

        foreach (var word in new[]
                 {
                     "IDLE", "HIDDEN", "DISABLED", "OFF", "LOCAL",
                 })
        {
            ApplyWordStyle(
                text,
                foreground,
                background,
                word,
                ConsoleColor.DarkGray);
        }
    }

    private static void ApplyHotkeyStyles(
        string text,
        ConsoleColor[] foreground,
        ConsoleColor[] background)
    {
        var index = 0;
        while (index < text.Length)
        {
            while (index < text.Length && char.IsWhiteSpace(text[index]))
            {
                index++;
            }

            var tokenStart = index;
            while (index < text.Length && !char.IsWhiteSpace(text[index]))
            {
                index++;
            }

            if (tokenStart == index)
            {
                continue;
            }

            var token = text[tokenStart..index].TrimEnd('.', ',', ';', ':');
            if (token.Contains('+') &&
                (token.StartsWith("Ctrl+", StringComparison.OrdinalIgnoreCase) ||
                 token.StartsWith("Alt+", StringComparison.OrdinalIgnoreCase) ||
                 token.StartsWith("Shift+", StringComparison.OrdinalIgnoreCase) ||
                 token.StartsWith("Win+", StringComparison.OrdinalIgnoreCase)))
            {
                ApplyStyle(
                    foreground,
                    background,
                    tokenStart,
                    token.Length,
                    ConsoleColor.DarkCyan,
                    ConsoleColor.Black);
            }
        }
    }

    private static void ApplyPrefixStyle(
        string text,
        ConsoleColor[] foreground,
        ConsoleColor[] background,
        string prefix,
        ConsoleColor color)
    {
        if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            ApplyStyle(
                foreground,
                background,
                0,
                Math.Min(prefix.Length, text.Length),
                color,
                ConsoleColor.Black);
        }
    }

    private static void ApplyPhraseStyle(
        string text,
        ConsoleColor[] foreground,
        ConsoleColor[] background,
        string phrase,
        ConsoleColor color)
    {
        var searchStart = 0;
        while (searchStart < text.Length)
        {
            var index = text.IndexOf(
                phrase,
                searchStart,
                StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                break;
            }

            ApplyStyle(
                foreground,
                background,
                index,
                phrase.Length,
                color,
                ConsoleColor.Black);
            searchStart = index + phrase.Length;
        }
    }

    private static void ApplyWordStyle(
        string text,
        ConsoleColor[] foreground,
        ConsoleColor[] background,
        string word,
        ConsoleColor color)
    {
        var searchStart = 0;
        while (searchStart < text.Length)
        {
            var index = text.IndexOf(
                word,
                searchStart,
                StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                break;
            }

            var beforeBoundary = index == 0 || !IsWordCharacter(text[index - 1]);
            var afterIndex = index + word.Length;
            var afterBoundary = afterIndex >= text.Length ||
                !IsWordCharacter(text[afterIndex]);

            if (beforeBoundary && afterBoundary)
            {
                ApplyStyle(
                    foreground,
                    background,
                    index,
                    word.Length,
                    color,
                    ConsoleColor.Black);
            }

            searchStart = index + word.Length;
        }
    }

    private static bool IsWordCharacter(char character)
    {
        return char.IsLetterOrDigit(character) || character == '_';
    }

    private static void ApplyStyle(
        ConsoleColor[] foreground,
        ConsoleColor[] background,
        int start,
        int length,
        ConsoleColor foregroundColor,
        ConsoleColor backgroundColor)
    {
        var end = Math.Min(foreground.Length, Math.Max(start, start + length));
        for (var index = Math.Max(0, start); index < end; index++)
        {
            foreground[index] = foregroundColor;
            background[index] = backgroundColor;
        }
    }

    private static bool IsNavigationLine(string value)
    {
        return value.StartsWith("↑/↓", StringComparison.Ordinal) ||
            value.StartsWith("Enter opens", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSectionHeading(string value)
    {
        if (value.Length is < 3 or > 40)
        {
            return false;
        }

        var containsLetter = false;
        foreach (var character in value)
        {
            if (char.IsLetter(character))
            {
                containsLetter = true;
                if (char.IsLower(character))
                {
                    return false;
                }
            }
            else if (!char.IsWhiteSpace(character) &&
                     character is not '&' and not '/' and not '-')
            {
                return false;
            }
        }

        return containsLetter;
    }

    private static bool LooksLikePath(string value)
    {
        return value.StartsWith(@"\\", StringComparison.Ordinal) ||
            value.StartsWith("%", StringComparison.Ordinal) ||
            (value.Length >= 3 &&
             char.IsLetter(value[0]) &&
             value[1] == ':' &&
             (value[2] == '\\' || value[2] == '/'));
    }

    private static void WriteStyledLine(StyledConsoleLine line)
    {
        foreach (var span in line.Spans)
        {
            Console.ForegroundColor = span.Foreground;
            Console.BackgroundColor = span.Background;
            Console.Write(span.Text);
        }
    }

    private void PreparePrompt()
    {
        try
        {
            Console.ResetColor();
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
            ConsolePage.Background => "BACKGROUND AND STARTUP",
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

    private static string FormatBackendMode(CaptureBackendMode mode)
    {
        return mode switch
        {
            CaptureBackendMode.Auto => "Auto",
            CaptureBackendMode.ReferenceGdiPlus => "Reference GDI+",
            CaptureBackendMode.NativeGdiWic => "Native GDI + WIC",
            _ => mode.ToString(),
        };
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

    private sealed record StyledConsoleSpan(
        string Text,
        ConsoleColor Foreground,
        ConsoleColor Background);

    private sealed record StyledConsoleLine(
        string Text,
        IReadOnlyList<StyledConsoleSpan> Spans,
        string Signature)
    {
        public static StyledConsoleLine CreatePlain(
            string text,
            ConsoleColor foreground,
            ConsoleColor background)
        {
            return new StyledConsoleLine(
                text,
                [new StyledConsoleSpan(text, foreground, background)],
                $"{text}\u001f{(int)foreground}:{(int)background}");
        }

        public static StyledConsoleLine Create(
            string text,
            IReadOnlyList<ConsoleColor> foreground,
            IReadOnlyList<ConsoleColor> background)
        {
            if (text.Length == 0)
            {
                return CreatePlain(
                    string.Empty,
                    ConsoleColor.Gray,
                    ConsoleColor.Black);
            }

            var spans = new List<StyledConsoleSpan>();
            var signature = new System.Text.StringBuilder(text.Length + 64);
            signature.Append(text).Append('\u001f');
            var spanStart = 0;

            for (var index = 1; index <= text.Length; index++)
            {
                var boundary = index == text.Length ||
                    foreground[index] != foreground[spanStart] ||
                    background[index] != background[spanStart];

                if (!boundary)
                {
                    continue;
                }

                var spanText = text[spanStart..index];
                var spanForeground = foreground[spanStart];
                var spanBackground = background[spanStart];
                spans.Add(new StyledConsoleSpan(
                    spanText,
                    spanForeground,
                    spanBackground));
                signature
                    .Append(spanStart)
                    .Append(':')
                    .Append(index - spanStart)
                    .Append(':')
                    .Append((int)spanForeground)
                    .Append(':')
                    .Append((int)spanBackground)
                    .Append(';');
                spanStart = index;
            }

            return new StyledConsoleLine(
                text,
                spans,
                signature.ToString());
        }
    }
}
