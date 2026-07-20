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
        TrySetWindowTitle();
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
        TrySetWindowTitle();
        RenderDiff(BuildFrame(model));
    }

    public string? PromptForText(
        string title,
        string currentValue,
        string instructions)
    {
        PreparePrompt();

        try
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(title);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine(new string(
                '─',
                Math.Min(78, Math.Max(20, SafeWindowWidth() - 1))));
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write("Current   ");
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine(currentValue);
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine(instructions);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("Leave empty to cancel.");
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write("New       > ");
            Console.ForegroundColor = ConsoleColor.White;
            return Console.ReadLine();
        }
        finally
        {
            Console.ResetColor();
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
            // The console may already be unavailable during process teardown.
        }
    }

    private IReadOnlyList<StyledConsoleLine> BuildFrame(ConsoleViewModel model)
    {
        var width = SafeWindowWidth();
        var height = SafeWindowHeight();
        var contentWidth = Math.Max(1, width - 1);

        if (width < MinimumUsefulWidth || height < MinimumUsefulHeight)
        {
            return
            [
                Plain("SCapturer", ConsoleColor.Cyan),
                Blank(),
                Plain(
                    $"Console window is too small ({width}×{height}).",
                    ConsoleColor.Yellow),
                Plain(
                    $"Resize it to at least {MinimumUsefulWidth}×{MinimumUsefulHeight}.",
                    ConsoleColor.Gray),
                Blank(),
                StatusLine(model.StatusMessage, LastStatusLevel(model)),
            ];
        }

        var lines = new List<StyledConsoleLine>(52)
        {
            CenteredTitleLine($"SCAPTURER · {PageTitle(_currentPage)}", contentWidth),
            RuleLine(contentWidth),
            SectionLine("RUNTIME"),
        };

        AddHeader(lines, model, contentWidth);
        lines.Add(Blank());
        AddPageContent(lines, model, contentWidth, height);
        lines.Add(RuleLine(contentWidth));
        AddFooter(lines, model, contentWidth);

        return lines;
    }

    private static void AddHeader(
        ICollection<StyledConsoleLine> lines,
        ConsoleViewModel model,
        int width)
    {
        lines.Add(FieldRow(
            width,
            new FieldCell("Runtime", "ACTIVE", ConsoleColor.Green),
            new FieldCell(
                "Console",
                model.ConsoleVisible ? "VISIBLE" : "HIDDEN",
                model.ConsoleVisible ? ConsoleColor.Green : ConsoleColor.DarkGray),
            new FieldCell(
                "Pipeline",
                FormatPipeline(model.Pipeline),
                PipelineColor(model.Pipeline))));

        lines.Add(FieldRow(
            width,
            new FieldCell(
                "Backend",
                model.BackendSelection.ActiveName,
                BackendColor(model.BackendSelection.ActiveKind)),
            new FieldCell(
                "Requested",
                FormatBackendModeToken(model.Settings.CaptureBackend),
                ConsoleColor.DarkCyan),
            new FieldCell(
                "Benchmark",
                model.BenchmarkInProgress ? "RUNNING" : "IDLE",
                model.BenchmarkInProgress ? ConsoleColor.Cyan : ConsoleColor.DarkGray)));

        lines.Add(FieldRow(
            width,
            new FieldCell(
                "Displays",
                model.Topology.MonitorCount.ToString(),
                ConsoleColor.Gray),
            new FieldCell("DPI", model.Topology.DpiMode, ConsoleColor.Gray),
            new FieldCell(
                "Session",
                model.Topology.IsRemoteSession ? "REMOTE" : "LOCAL",
                model.Topology.IsRemoteSession ? ConsoleColor.Yellow : ConsoleColor.DarkGray)));

        lines.Add(FieldRow(
            width,
            new FieldCell(
                "Desktop",
                $"{model.Topology.VirtualBounds.Width}×{model.Topology.VirtualBounds.Height}",
                ConsoleColor.Gray),
            new FieldCell(
                "Origin",
                $"{model.Topology.VirtualBounds.X},{model.Topology.VirtualBounds.Y}",
                ConsoleColor.Gray),
            new FieldCell(
                "Topology",
                $"v{model.Topology.Version}",
                ConsoleColor.Gray)));

        if (model.BackendSelection.IsFallback)
        {
            lines.Add(FieldRow(
                width,
                new FieldCell("Fallback", "YES", ConsoleColor.Yellow),
                new FieldCell(
                    "Reason",
                    model.BackendSelection.FallbackReason ?? "Native backend unavailable",
                    ConsoleColor.Yellow)));
        }
    }

    private void AddPageContent(
        ICollection<StyledConsoleLine> lines,
        ConsoleViewModel model,
        int width,
        int height)
    {
        switch (_currentPage)
        {
            case ConsolePage.Dashboard:
                AddDashboard(lines, model, width, height);
                break;
            case ConsolePage.CaptureSettings:
                AddCaptureSettings(lines, model, width);
                break;
            case ConsolePage.Hotkeys:
                AddHotkeys(lines, model, width);
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
                AddAbout(lines, model, width);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private void AddDashboard(
        ICollection<StyledConsoleLine> lines,
        ConsoleViewModel model,
        int width,
        int height)
    {
        lines.Add(SectionLine("LAST CAPTURE"));

        if (model.LastCapture is null)
        {
            lines.Add(FieldRow(
                width,
                new FieldCell("State", "NONE", ConsoleColor.DarkGray)));
        }
        else
        {
            var last = model.LastCapture;
            lines.Add(FieldRow(
                width,
                new FieldCell(
                    "Kind",
                    last.Kind == CaptureKind.Region ? "REGION" : "FULL",
                    ConsoleColor.Cyan),
                new FieldCell(
                    "Dimensions",
                    $"{last.Width}×{last.Height}",
                    ConsoleColor.Gray),
                new FieldCell(
                    "Size",
                    FormatBytes(last.FileSizeBytes),
                    ConsoleColor.Gray)));

            lines.Add(FieldRow(
                width,
                new FieldCell(
                    "Backend",
                    last.BackendName,
                    BackendColor(last.BackendKind)),
                new FieldCell(
                    "Total",
                    $"{last.Metrics.TotalMilliseconds:0.0} ms",
                    ConsoleColor.Green),
                new FieldCell(
                    "PNG",
                    $"{last.Metrics.PngPersistenceMilliseconds:0.0} ms",
                    ConsoleColor.Gray)));

            lines.Add(LabelValueLine(
                "File",
                TruncateMiddle(last.FilePath, Math.Max(8, width - 13)),
                ConsoleColor.DarkGray));

            if (last.Warnings is { Count: > 0 })
            {
                lines.Add(LabelValueLine(
                    "Warnings",
                    last.Warnings.Count.ToString(),
                    ConsoleColor.Yellow));
            }
        }

        lines.Add(Blank());
        lines.Add(SectionLine("COMMANDS"));
        AddMenu(lines, model, width);

        if (height >= 32 && model.Events.Count > 0)
        {
            lines.Add(Blank());
            lines.Add(SectionLine("EVENTS"));

            foreach (var eventItem in model.Events
                         .Reverse()
                         .Take(3))
            {
                lines.Add(EventLine(eventItem, width));
            }
        }
    }

    private void AddCaptureSettings(
        ICollection<StyledConsoleLine> lines,
        ConsoleViewModel model,
        int width)
    {
        lines.Add(SectionLine("SETTINGS"));
        AddMenu(lines, model, width);
        lines.Add(Blank());
        lines.Add(SectionLine("EFFECTIVE"));
        lines.Add(FieldRow(
            width,
            new FieldCell(
                "Backend",
                model.BackendSelection.ActiveName,
                BackendColor(model.BackendSelection.ActiveKind)),
            new FieldCell(
                "Fallback",
                model.BackendSelection.IsFallback ? "YES" : "NO",
                model.BackendSelection.IsFallback
                    ? ConsoleColor.Yellow
                    : ConsoleColor.DarkGray)));
        lines.Add(FieldRow(
            width,
            new FieldCell("Format", "PNG", ConsoleColor.Gray),
            new FieldCell("Pixels", "PHYSICAL", ConsoleColor.Gray),
            new FieldCell("Encoding", "LOSSLESS", ConsoleColor.Gray)));

        if (model.BackendSelection.IsFallback)
        {
            lines.Add(LabelValueLine(
                "Reason",
                TruncateMiddle(
                    model.BackendSelection.FallbackReason ?? "Native backend unavailable",
                    Math.Max(8, width - 13)),
                ConsoleColor.Yellow));
        }
    }

    private void AddHotkeys(
        ICollection<StyledConsoleLine> lines,
        ConsoleViewModel model,
        int width)
    {
        lines.Add(SectionLine("REGISTERED HOTKEYS"));
        AddMenu(lines, model, width);
        lines.Add(Blank());
        lines.Add(Plain(
            "Enter edits the selected binding. SCapturer validates duplicates and Windows registration.",
            ConsoleColor.DarkGray));
    }

    private void AddSaveLocations(
        ICollection<StyledConsoleLine> lines,
        ConsoleViewModel model,
        int width)
    {
        lines.Add(SectionLine("CURRENT PATHS"));
        lines.Add(LabelValueLine(
            "Full",
            TruncateMiddle(model.Settings.FullCaptureFolder, Math.Max(8, width - 13)),
            ConsoleColor.DarkGray));
        lines.Add(LabelValueLine(
            "Region",
            TruncateMiddle(model.Settings.SnipCaptureFolder, Math.Max(8, width - 13)),
            ConsoleColor.DarkGray));
        lines.Add(Blank());
        lines.Add(SectionLine("ACTIONS"));
        AddMenu(lines, model, width);
    }

    private void AddDiagnostics(
        ICollection<StyledConsoleLine> lines,
        ConsoleViewModel model,
        int width)
    {
        lines.Add(SectionLine("STATE"));
        lines.Add(FieldRow(
            width,
            new FieldCell(
                "Diagnostics",
                OnOff(model.Settings.EnableDiagnostics),
                model.Settings.EnableDiagnostics ? ConsoleColor.Green : ConsoleColor.DarkGray),
            new FieldCell(
                "Benchmark",
                model.BenchmarkInProgress ? "RUNNING" : "IDLE",
                model.BenchmarkInProgress ? ConsoleColor.Cyan : ConsoleColor.DarkGray),
            new FieldCell(
                "Pipeline",
                FormatPipeline(model.Pipeline),
                PipelineColor(model.Pipeline))));

        lines.Add(LabelValueLine(
            "Metrics",
            TruncateMiddle(_paths.CaptureMetricsFile, Math.Max(8, width - 13)),
            ConsoleColor.DarkGray));
        lines.Add(LabelValueLine(
            "Reports",
            TruncateMiddle(_paths.BenchmarkReportsDirectory, Math.Max(8, width - 13)),
            ConsoleColor.DarkGray));
        lines.Add(Plain(
            "Gate       ≥20% p95 or allocation improvement; native median regression must stay ≤5%.",
            ConsoleColor.DarkGray));
        lines.Add(Blank());
        lines.Add(SectionLine("ACTIONS"));
        AddMenu(lines, model, width);
    }

    private void AddRecentCaptures(
        ICollection<StyledConsoleLine> lines,
        ConsoleViewModel model,
        int width)
    {
        lines.Add(SectionLine("CAPTURES"));

        if (model.RecentCaptures.Count == 0)
        {
            lines.Add(Plain(
                "No PNG captures were found in the configured folders.",
                ConsoleColor.DarkGray));
        }
        else
        {
            lines.Add(Plain(
                "    TIME       KIND     SIZE       FILE",
                ConsoleColor.DarkGray));
        }

        AddMenu(lines, model, width);

        var selectedRecent = GetSelectedRecent(model);
        if (selectedRecent is not null)
        {
            lines.Add(Blank());
            lines.Add(SectionLine("SELECTED"));
            lines.Add(LabelValueLine(
                "Path",
                TruncateMiddle(selectedRecent.FilePath, Math.Max(8, width - 13)),
                ConsoleColor.DarkGray));
            lines.Add(LabelValueLine(
                "Modified",
                selectedRecent.LastWriteTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                ConsoleColor.Gray));
        }
    }

    private void AddBackground(
        ICollection<StyledConsoleLine> lines,
        ConsoleViewModel model,
        int width)
    {
        var startupState = model.Autostart.ErrorMessage is not null
            ? "ERROR"
            : model.Autostart.IsEnabled
                ? model.Autostart.IsCurrent ? "ENABLED" : "STALE"
                : "DISABLED";

        lines.Add(SectionLine("STATE"));
        lines.Add(FieldRow(
            width,
            new FieldCell(
                "Console",
                model.ConsoleVisible ? "VISIBLE" : "HIDDEN",
                model.ConsoleVisible ? ConsoleColor.Green : ConsoleColor.DarkGray),
            new FieldCell(
                "Launch",
                model.StartedInBackground ? "BACKGROUND" : "INTERACTIVE",
                model.StartedInBackground ? ConsoleColor.Cyan : ConsoleColor.Gray),
            new FieldCell(
                "Autostart",
                startupState,
                StateColor(startupState))));

        lines.Add(FieldRow(
            width,
            new FieldCell(
                "Close X",
                "BACKGROUND HANDOFF",
                ConsoleColor.Cyan),
            new FieldCell(
                "Hotkey",
                HotkeyBindingService.Format(model.Settings.ToggleConsoleHotkey),
                ConsoleColor.DarkCyan)));

        lines.Add(LabelValueLine(
            "Command",
            TruncateMiddle(model.Autostart.ExpectedCommand, Math.Max(8, width - 13)),
            ConsoleColor.DarkGray));

        if (!string.IsNullOrWhiteSpace(model.Autostart.ErrorMessage))
        {
            lines.Add(LabelValueLine(
                "Error",
                TruncateMiddle(model.Autostart.ErrorMessage!, Math.Max(8, width - 13)),
                ConsoleColor.Red));
        }
        else if (model.Autostart.IsEnabled && !model.Autostart.IsCurrent)
        {
            lines.Add(LabelValueLine(
                "Note",
                "Registration points to another build or executable path.",
                ConsoleColor.Yellow));
        }

        lines.Add(Blank());
        lines.Add(SectionLine("ACTIONS"));
        AddMenu(lines, model, width);
    }

    private void AddAbout(
        ICollection<StyledConsoleLine> lines,
        ConsoleViewModel model,
        int width)
    {
        lines.Add(SectionLine("SCAPTURER"));
        lines.Add(FieldRow(
            width,
            new FieldCell("Author", "XCON", ConsoleColor.Cyan),
            new FieldCell("Project", "X-LAB", ConsoleColor.DarkCyan),
            new FieldCell("Runtime", ".NET 8", ConsoleColor.Gray)));
        lines.Add(Blank());
        lines.Add(LabelValueLine(
            "Purpose",
            "Performance-first Windows screenshot developer utility.",
            ConsoleColor.Gray));
        lines.Add(LabelValueLine(
            "Capture",
            "Reference GDI+ and native GDI + WIC backends.",
            ConsoleColor.Gray));
        lines.Add(LabelValueLine(
            "Storage",
            "Atomic PNG commit and isolated clipboard dispatcher.",
            ConsoleColor.Gray));
        lines.Add(LabelValueLine(
            "Geometry",
            "Per-Monitor V2 physical virtual-desktop coordinates.",
            ConsoleColor.Gray));
        lines.Add(LabelValueLine(
            "Lifecycle",
            "Background mode, single-instance IPC and user autostart.",
            ConsoleColor.Gray));
        lines.Add(Blank());
        lines.Add(SectionLine("NAVIGATION"));
        AddMenu(lines, model, width);
    }

    private void AddMenu(
        ICollection<StyledConsoleLine> lines,
        ConsoleViewModel model,
        int width)
    {
        var entries = BuildMenuEntries(model);
        ClampSelection(entries);
        var selected = GetSelection();

        for (var index = 0; index < entries.Count; index++)
        {
            lines.Add(MenuLine(
                entries[index],
                index,
                index == selected,
                width));
        }
    }

    private static StyledConsoleLine MenuLine(
        MenuEntry entry,
        int index,
        bool selected,
        int width)
    {
        var marker = selected ? "›" : " ";
        var number = index switch
        {
            < 9 => $"{index + 1}.",
            9 => "0.",
            _ => "  ",
        };
        var prefix = $"{marker} {number} ";
        var right = entry.RightText ?? string.Empty;
        var rightReserve = string.IsNullOrEmpty(right) ? 0 : right.Length + 2;
        var labelWidth = Math.Max(1, width - prefix.Length - rightReserve);
        var label = Truncate(entry.Label, labelWidth).PadRight(labelWidth);
        var background = selected ? ConsoleColor.DarkGray : ConsoleColor.Black;

        var spans = new List<StyledConsoleSpan>
        {
            new(
                prefix,
                selected ? ConsoleColor.Cyan : ConsoleColor.DarkGray,
                background),
            new(
                label,
                selected
                    ? ConsoleColor.White
                    : entry.IsDisabled ? ConsoleColor.DarkGray : ConsoleColor.Gray,
                background),
        };

        if (!string.IsNullOrEmpty(right))
        {
            spans.Add(new StyledConsoleSpan(
                "  " + right,
                selected
                    ? ConsoleColor.White
                    : entry.RightColor,
                background));
        }

        var currentLength = spans.Sum(span => span.Text.Length);
        if (currentLength < width)
        {
            spans.Add(new StyledConsoleSpan(
                new string(' ', width - currentLength),
                ConsoleColor.Gray,
                background));
        }

        return StyledConsoleLine.Create(spans);
    }

    private void AddFooter(
        ICollection<StyledConsoleLine> lines,
        ConsoleViewModel model,
        int width)
    {
        var statusLevel = LastStatusLevel(model);
        var statusText = Truncate(model.StatusMessage, Math.Max(8, width - 7));
        lines.Add(StatusLine(statusText, statusLevel));
        lines.Add(Plain(FooterNavigation(model), ConsoleColor.DarkGray));
    }

    private string FooterNavigation(ConsoleViewModel model)
    {
        return _currentPage switch
        {
            ConsolePage.Dashboard =>
                $"↑↓ Select   Enter Execute   1–9/0 Direct   " +
                $"{HotkeyBindingService.Format(model.Settings.ToggleConsoleHotkey)} Hide",
            ConsolePage.RecentCaptures =>
                "↑↓ Select   Enter Open   F Folder   R Refresh   Esc Back",
            ConsolePage.Hotkeys =>
                "↑↓ Select   Enter Edit   Esc Back",
            _ =>
                "↑↓ Select   Enter Execute   Esc Back   1–9/0 Direct",
        };
    }

    private IReadOnlyList<MenuEntry> BuildMenuEntries(ConsoleViewModel model)
    {
        return _currentPage switch
        {
            ConsolePage.Dashboard =>
            [
                Entry(
                    "Capture full desktop",
                    ConsoleAction.CaptureFull,
                    HotkeyBindingService.Format(model.Settings.FullCaptureHotkey)),
                Entry(
                    "Capture selected region",
                    ConsoleAction.CaptureRegion,
                    HotkeyBindingService.Format(model.Settings.RegionCaptureHotkey)),
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
                    "Copy to clipboard",
                    ConsoleAction.ToggleClipboard,
                    OnOff(model.Settings.CopyToClipboard),
                    StateColor(OnOff(model.Settings.CopyToClipboard))),
                Entry(
                    "Capture sound",
                    ConsoleAction.ToggleSound,
                    OnOff(model.Settings.PlayCaptureSound),
                    StateColor(OnOff(model.Settings.PlayCaptureSound))),
                Entry(
                    "Requested backend",
                    ConsoleAction.CycleCaptureBackend,
                    FormatBackendModeToken(model.Settings.CaptureBackend),
                    ConsoleColor.DarkCyan),
                Entry("Back to dashboard", ConsoleAction.Back),
            ],

            ConsolePage.Hotkeys =>
            [
                Entry(
                    "Full capture",
                    ConsoleAction.EditFullHotkey,
                    HotkeyBindingService.Format(model.Settings.FullCaptureHotkey)),
                Entry(
                    "Region capture",
                    ConsoleAction.EditRegionHotkey,
                    HotkeyBindingService.Format(model.Settings.RegionCaptureHotkey)),
                Entry(
                    "Toggle console",
                    ConsoleAction.EditToggleConsoleHotkey,
                    HotkeyBindingService.Format(model.Settings.ToggleConsoleHotkey)),
                Entry(
                    "Exit",
                    ConsoleAction.EditExitHotkey,
                    HotkeyBindingService.Format(model.Settings.ExitHotkey)),
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
                    "Capture diagnostics",
                    ConsoleAction.ToggleDiagnostics,
                    OnOff(model.Settings.EnableDiagnostics),
                    StateColor(OnOff(model.Settings.EnableDiagnostics))),
                Entry(
                    model.BenchmarkInProgress
                        ? "Selected-backend benchmark running"
                        : "Run selected-backend baseline benchmark",
                    model.BenchmarkInProgress
                        ? ConsoleAction.None
                        : ConsoleAction.RunBenchmark,
                    isDisabled: model.BenchmarkInProgress),
                Entry(
                    model.BenchmarkInProgress
                        ? "Backend comparison unavailable while benchmark runs"
                        : "Compare Reference GDI+ vs Native GDI + WIC",
                    model.BenchmarkInProgress
                        ? ConsoleAction.None
                        : ConsoleAction.RunBackendComparison,
                    isDisabled: model.BenchmarkInProgress),
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
                $"{item.LastWriteTime.ToLocalTime():HH:mm:ss}   {kind,-7} " +
                $"{FormatBytes(item.FileSizeBytes),9}   {item.FileName}",
                new ConsoleCommand(ConsoleAction.OpenRecentCapture, index),
                RecentIndex: index));
        }

        entries.Add(Entry("Refresh recent captures", ConsoleAction.RefreshRecentCaptures));
        entries.Add(Entry("Back to dashboard", ConsoleAction.Back));
        return entries;
    }

    private RecentCaptureItem? GetSelectedRecent(ConsoleViewModel model)
    {
        var entries = BuildMenuEntries(model);
        if (entries.Count == 0)
        {
            return null;
        }

        ClampSelection(entries);
        var recentIndex = entries[GetSelection()].RecentIndex;
        return recentIndex >= 0 && recentIndex < model.RecentCaptures.Count
            ? model.RecentCaptures[recentIndex]
            : null;
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

    private void RenderDiff(IReadOnlyList<StyledConsoleLine> sourceLines)
    {
        var width = SafeWindowWidth();
        var height = SafeWindowHeight();
        var contentWidth = Math.Max(1, width - 1);

        var normalized = sourceLines
            .Take(height)
            .Select(line => FitStyledLine(line, contentWidth))
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
                    : Plain(new string(' ', contentWidth), ConsoleColor.Gray);

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

    private static StyledConsoleLine FitStyledLine(
        StyledConsoleLine line,
        int width)
    {
        var spans = new List<StyledConsoleSpan>();
        var remaining = width;

        foreach (var span in line.Spans)
        {
            if (remaining <= 0)
            {
                break;
            }

            var text = span.Text.Length <= remaining
                ? span.Text
                : span.Text[..remaining];

            if (text.Length > 0)
            {
                spans.Add(span with { Text = text });
                remaining -= text.Length;
            }
        }

        if (remaining > 0)
        {
            spans.Add(new StyledConsoleSpan(
                new string(' ', remaining),
                ConsoleColor.Gray,
                ConsoleColor.Black));
        }

        return StyledConsoleLine.Create(spans);
    }

    private static StyledConsoleLine FieldRow(
        int width,
        params FieldCell[] fields)
    {
        if (fields.Length == 0)
        {
            return Blank();
        }

        const int labelWidth = 11;
        const int gapWidth = 2;
        var availableForValues = Math.Max(
            fields.Length,
            width - fields.Length * labelWidth - (fields.Length - 1) * gapWidth);
        var valueWidth = Math.Max(1, availableForValues / fields.Length);
        var spans = new List<StyledConsoleSpan>(fields.Length * 3);

        for (var index = 0; index < fields.Length; index++)
        {
            var field = fields[index];
            spans.Add(new StyledConsoleSpan(
                field.Label.PadRight(labelWidth),
                ConsoleColor.DarkGray,
                ConsoleColor.Black));
            spans.Add(new StyledConsoleSpan(
                Truncate(field.Value, valueWidth).PadRight(valueWidth),
                field.ValueColor,
                ConsoleColor.Black));

            if (index < fields.Length - 1)
            {
                spans.Add(new StyledConsoleSpan(
                    new string(' ', gapWidth),
                    ConsoleColor.Gray,
                    ConsoleColor.Black));
            }
        }

        return StyledConsoleLine.Create(spans);
    }

    private static StyledConsoleLine LabelValueLine(
        string label,
        string value,
        ConsoleColor valueColor)
    {
        return StyledConsoleLine.Create(
        [
            new StyledConsoleSpan(
                label.PadRight(11),
                ConsoleColor.DarkGray,
                ConsoleColor.Black),
            new StyledConsoleSpan(
                value,
                valueColor,
                ConsoleColor.Black),
        ]);
    }

    private static StyledConsoleLine EventLine(
        ConsoleEventItem item,
        int width)
    {
        var token = EventToken(item.Level);
        var time = item.Timestamp.ToLocalTime().ToString("HH:mm:ss");
        var messageWidth = Math.Max(1, width - 17);

        return StyledConsoleLine.Create(
        [
            new StyledConsoleSpan(
                time + "  ",
                ConsoleColor.DarkGray,
                ConsoleColor.Black),
            new StyledConsoleSpan(
                token.PadRight(6),
                EventColor(item.Level),
                ConsoleColor.Black),
            new StyledConsoleSpan(
                Truncate(item.Message, messageWidth),
                ConsoleColor.Gray,
                ConsoleColor.Black),
        ]);
    }

    private static StyledConsoleLine StatusLine(
        string message,
        ConsoleEventLevel level)
    {
        var token = EventToken(level);
        return StyledConsoleLine.Create(
        [
            new StyledConsoleSpan(
                token.PadRight(6),
                EventColor(level),
                ConsoleColor.Black),
            new StyledConsoleSpan(
                message,
                ConsoleColor.Gray,
                ConsoleColor.Black),
        ]);
    }

    private static StyledConsoleLine CenteredTitleLine(
        string text,
        int width)
    {
        var truncated = Truncate(text, width);
        var leftPadding = Math.Max(0, (width - truncated.Length) / 2);
        return StyledConsoleLine.Create(
        [
            new StyledConsoleSpan(
                new string(' ', leftPadding),
                ConsoleColor.Cyan,
                ConsoleColor.Black),
            new StyledConsoleSpan(
                truncated,
                ConsoleColor.Cyan,
                ConsoleColor.Black),
        ]);
    }

    private static StyledConsoleLine SectionLine(string title)
    {
        return Plain(title, ConsoleColor.DarkCyan);
    }

    private static StyledConsoleLine RuleLine(int width)
    {
        return Plain(
            new string('─', Math.Max(1, width)),
            ConsoleColor.DarkGray);
    }

    private static StyledConsoleLine Blank()
    {
        return Plain(string.Empty, ConsoleColor.Gray);
    }

    private static StyledConsoleLine Plain(
        string text,
        ConsoleColor foreground)
    {
        return StyledConsoleLine.Create(
        [
            new StyledConsoleSpan(
                text,
                foreground,
                ConsoleColor.Black),
        ]);
    }

    private static ConsoleEventLevel LastStatusLevel(ConsoleViewModel model)
    {
        return model.Events.Count > 0
            ? model.Events[^1].Level
            : ConsoleEventLevel.Info;
    }

    private static ConsoleColor PipelineColor(CapturePipelineSnapshot pipeline)
    {
        return pipeline.State.ToString().Equals("Idle", StringComparison.OrdinalIgnoreCase)
            ? ConsoleColor.DarkGray
            : ConsoleColor.Cyan;
    }

    private static ConsoleColor BackendColor(CaptureBackendKind kind)
    {
        return kind == CaptureBackendKind.NativeGdiWic
            ? ConsoleColor.Cyan
            : ConsoleColor.DarkCyan;
    }

    private static ConsoleColor StateColor(string state)
    {
        return state.ToUpperInvariant() switch
        {
            "ACTIVE" or "VISIBLE" or "ENABLED" or "ON" or "PASS" or "YES" =>
                ConsoleColor.Green,
            "RUNNING" or "BACKGROUND" or "HANDOFF" or "QUEUED" =>
                ConsoleColor.Cyan,
            "WARNING" or "WARN" or "FALLBACK" or "STALE" or "COALESCED" =>
                ConsoleColor.Yellow,
            "ERROR" or "FAILED" or "FAIL" or "REJECTED" =>
                ConsoleColor.Red,
            _ => ConsoleColor.DarkGray,
        };
    }

    private static string EventToken(ConsoleEventLevel level)
    {
        return level switch
        {
            ConsoleEventLevel.Success => "OK",
            ConsoleEventLevel.Warning => "WARN",
            ConsoleEventLevel.Error => "ERROR",
            _ => "INFO",
        };
    }

    private static ConsoleColor EventColor(ConsoleEventLevel level)
    {
        return level switch
        {
            ConsoleEventLevel.Success => ConsoleColor.Green,
            ConsoleEventLevel.Warning => ConsoleColor.Yellow,
            ConsoleEventLevel.Error => ConsoleColor.Red,
            _ => ConsoleColor.Cyan,
        };
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
            // ReadLine may still work in hosts without cursor control.
        }
    }

    private static MenuEntry Entry(
        string label,
        ConsoleAction action,
        string? rightText = null,
        ConsoleColor rightColor = ConsoleColor.DarkCyan,
        bool isDisabled = false)
    {
        return new MenuEntry(
            label,
            new ConsoleCommand(action),
            RecentIndex: -1,
            RightText: rightText,
            RightColor: rightColor,
            IsDisabled: isDisabled);
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

    private static string WindowPageTitle(ConsolePage page)
    {
        return page switch
        {
            ConsolePage.Dashboard => "Dashboard",
            ConsolePage.CaptureSettings => "Capture Settings",
            ConsolePage.Hotkeys => "Hotkeys",
            ConsolePage.SaveLocations => "Save Locations",
            ConsolePage.Diagnostics => "Diagnostics",
            ConsolePage.RecentCaptures => "Recent Captures",
            ConsolePage.Background => "Background and Startup",
            ConsolePage.About => "About",
            _ => page.ToString(),
        };
    }

    private void TrySetWindowTitle()
    {
        try
        {
            Console.Title = $"SCapturer — {WindowPageTitle(_currentPage)}";
        }
        catch (Exception exception)
            when (exception is IOException or InvalidOperationException)
        {
            // Some hosts do not expose a mutable console title.
        }
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

        return $"{state}{active} +1 {pending}".TrimEnd();
    }

    private static string FormatKind(CaptureKind kind)
    {
        return kind == CaptureKind.Region ? "REGION" : "FULL";
    }

    private static string FormatBackendModeToken(CaptureBackendMode mode)
    {
        return mode switch
        {
            CaptureBackendMode.Auto => "AUTO",
            CaptureBackendMode.ReferenceGdiPlus => "REFERENCE",
            CaptureBackendMode.NativeGdiWic => "NATIVE",
            _ => mode.ToString().ToUpperInvariant(),
        };
    }

    private static string OnOff(bool value) => value ? "ON" : "OFF";

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

    private static string TruncateMiddle(string value, int width)
    {
        if (width <= 0)
        {
            return string.Empty;
        }

        if (value.Length <= width)
        {
            return value;
        }

        if (width <= 3)
        {
            return Truncate(value, width);
        }

        var rightLength = Math.Max(1, (width - 1) / 2);
        var leftLength = width - rightLength - 1;
        return value[..leftLength] + "…" + value[^rightLength..];
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
        int RecentIndex,
        string? RightText = null,
        ConsoleColor RightColor = ConsoleColor.DarkCyan,
        bool IsDisabled = false);

    private sealed record FieldCell(
        string Label,
        string Value,
        ConsoleColor ValueColor);

    private sealed record StyledConsoleSpan(
        string Text,
        ConsoleColor Foreground,
        ConsoleColor Background);

    private sealed record StyledConsoleLine(
        string Text,
        IReadOnlyList<StyledConsoleSpan> Spans,
        string Signature)
    {
        public static StyledConsoleLine Create(
            IReadOnlyList<StyledConsoleSpan> spans)
        {
            var text = string.Concat(spans.Select(span => span.Text));
            var signature = string.Join(
                "\u001e",
                spans.Select(span =>
                    $"{span.Text}\u001f{(int)span.Foreground}:{(int)span.Background}"));

            return new StyledConsoleLine(text, spans, signature);
        }
    }
}
