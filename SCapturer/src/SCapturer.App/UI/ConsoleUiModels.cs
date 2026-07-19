using SCapturer.Core.Capture;
using SCapturer.Core.Display;
using SCapturer.Core.Models;
using SCapturer.Core.Pipeline;

namespace SCapturer.App.UI;

internal enum ConsolePage
{
    Dashboard,
    CaptureSettings,
    Hotkeys,
    SaveLocations,
    Diagnostics,
    RecentCaptures,
    About,
}

internal enum ConsoleAction
{
    None,
    Redraw,
    Back,
    CaptureFull,
    CaptureRegion,
    OpenCaptureSettings,
    OpenHotkeys,
    OpenSaveLocations,
    OpenDiagnostics,
    OpenRecentCaptures,
    OpenAbout,
    ToggleClipboard,
    ToggleSound,
    ToggleDiagnostics,
    CycleCaptureBackend,
    EditFullHotkey,
    EditRegionHotkey,
    EditExitHotkey,
    RestoreDefaultHotkeys,
    ChangeFullFolder,
    ChangeSnipFolder,
    OpenFullFolder,
    OpenSnipFolder,
    RunBenchmark,
    RunBackendComparison,
    OpenDiagnosticsFolder,
    RefreshRecentCaptures,
    OpenRecentCapture,
    OpenRecentCaptureFolder,
    Exit,
}

internal sealed record ConsoleCommand(
    ConsoleAction Action,
    int ItemIndex = -1)
{
    public static ConsoleCommand None { get; } = new(ConsoleAction.None);

    public static ConsoleCommand Redraw { get; } = new(ConsoleAction.Redraw);
}

internal sealed record ConsoleViewModel(
    AppSettings Settings,
    string StatusMessage,
    CaptureResult? LastCapture,
    CapturePipelineSnapshot Pipeline,
    DisplayTopologySnapshot Topology,
    CaptureBackendSelection BackendSelection,
    bool BenchmarkInProgress,
    IReadOnlyList<RecentCaptureItem> RecentCaptures);
