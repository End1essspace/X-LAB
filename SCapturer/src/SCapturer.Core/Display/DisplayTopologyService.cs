using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SCapturer.Core.Display;

public sealed class DisplayTopologyService : IDisposable
{
    private const int MonitorInfoPrimary = 0x00000001;
    private const int SmRemoteSession = 0x1000;
    private static readonly TimeSpan DefaultStabilityTimeout = TimeSpan.FromSeconds(2);

    private readonly object _gate = new();
    private readonly System.Threading.Timer _refreshTimer;

    private DisplayTopologySnapshot _snapshot;
    private long _generation;
    private bool _transitioning;
    private bool _disposed;
    private bool _systemEventsSubscribed;
    private string _pendingReason = "Startup";

    public DisplayTopologyService()
    {
        _generation = 1;
        _snapshot = ReadSnapshot(_generation);
        _refreshTimer = new System.Threading.Timer(
            static state => ((DisplayTopologyService)state!).RefreshFromTimer(),
            this,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);

        SubscribeSystemEvents();
    }

    public event Action<DisplayTopologyChange>? TopologyChanged;

    public DisplayTopologySnapshot GetSnapshot()
    {
        lock (_gate)
        {
            return _snapshot;
        }
    }

    public DisplayTopologySnapshot AcquireStableSnapshot(TimeSpan? timeout = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var waitTimeout = timeout ?? DefaultStabilityTimeout;
        var deadline = DateTime.UtcNow + waitTimeout;

        while (true)
        {
            lock (_gate)
            {
                if (!_transitioning)
                {
                    return _snapshot;
                }
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw new DisplayTopologyUnavailableException(
                    "The Windows display topology did not stabilize before capture.");
            }

            Thread.Sleep(25);
        }
    }

    public bool IsCurrent(long version)
    {
        lock (_gate)
        {
            return !_transitioning && _generation == version;
        }
    }

    public DisplayTopologySnapshot RefreshNow(string reason = "Manual refresh")
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        long targetGeneration;

        lock (_gate)
        {
            _transitioning = true;
            targetGeneration = ++_generation;
            _pendingReason = reason;
        }

        return RefreshCore(targetGeneration, reason);
    }

    public void NotifyExternalChange(string reason)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        InvalidateAndSchedule(reason, TimeSpan.FromMilliseconds(150));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        UnsubscribeSystemEvents();
        _refreshTimer.Dispose();
    }

    private void SubscribeSystemEvents()
    {
        try
        {
            _systemEventsSubscribed = true;
            SystemEvents.DisplaySettingsChanging += OnDisplaySettingsChanging;
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            SystemEvents.SessionSwitch += OnSessionSwitch;
        }
        catch (InvalidOperationException)
        {
            UnsubscribeSystemEvents();
        }
        catch (ExternalException)
        {
            UnsubscribeSystemEvents();
        }
    }

    private void UnsubscribeSystemEvents()
    {
        if (!_systemEventsSubscribed)
        {
            return;
        }

        try
        {
            SystemEvents.DisplaySettingsChanging -= OnDisplaySettingsChanging;
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.SessionSwitch -= OnSessionSwitch;
        }
        catch (InvalidOperationException)
        {
            // System event infrastructure is unavailable during teardown.
        }
        catch (ExternalException)
        {
            // System event infrastructure is unavailable during teardown.
        }
        finally
        {
            _systemEventsSubscribed = false;
        }
    }

    private void OnDisplaySettingsChanging(object? sender, EventArgs e)
    {
        InvalidateAndSchedule(
            "Display settings are changing",
            TimeSpan.FromMilliseconds(250));
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        InvalidateAndSchedule(
            "Display settings changed",
            TimeSpan.FromMilliseconds(150));
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            InvalidateAndSchedule(
                "System resumed",
                TimeSpan.FromMilliseconds(400));
        }
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.RemoteConnect or
            SessionSwitchReason.RemoteDisconnect or
            SessionSwitchReason.SessionLogon or
            SessionSwitchReason.SessionUnlock or
            SessionSwitchReason.ConsoleConnect)
        {
            InvalidateAndSchedule(
                $"Session changed: {e.Reason}",
                TimeSpan.FromMilliseconds(300));
        }
    }

    private void InvalidateAndSchedule(string reason, TimeSpan delay)
    {
        DisplayTopologyChange change;

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _transitioning = true;
            _pendingReason = reason;
            var version = ++_generation;
            change = new DisplayTopologyChange(
                version,
                reason,
                IsStable: false,
                _snapshot);

            _refreshTimer.Change(delay, Timeout.InfiniteTimeSpan);
        }

        PublishChange(change);
    }

    private void RefreshFromTimer()
    {
        long targetGeneration;
        string reason;

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            targetGeneration = _generation;
            reason = _pendingReason;
        }

        try
        {
            RefreshCore(targetGeneration, reason);
        }
        catch
        {
            lock (_gate)
            {
                if (!_disposed)
                {
                    _refreshTimer.Change(
                        TimeSpan.FromMilliseconds(250),
                        Timeout.InfiniteTimeSpan);
                }
            }
        }
    }

    private DisplayTopologySnapshot RefreshCore(
        long targetGeneration,
        string reason)
    {
        var refreshed = ReadSnapshot(targetGeneration);
        DisplayTopologyChange change;

        lock (_gate)
        {
            if (_disposed)
            {
                return _snapshot;
            }

            if (targetGeneration != _generation)
            {
                _refreshTimer.Change(
                    TimeSpan.FromMilliseconds(150),
                    Timeout.InfiniteTimeSpan);
                return _snapshot;
            }

            var previous = _snapshot;
            _snapshot = refreshed;
            _transitioning = false;

            change = new DisplayTopologyChange(
                refreshed.Version,
                previous.StructurallyEquals(refreshed)
                    ? $"{reason}; topology confirmed"
                    : reason,
                IsStable: true,
                refreshed);
        }

        PublishChange(change);
        return refreshed;
    }

    private void PublishChange(DisplayTopologyChange change)
    {
        var handlers = TopologyChanged;
        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList()
                     .Cast<Action<DisplayTopologyChange>>())
        {
            try
            {
                handler(change);
            }
            catch
            {
                // Display observers must not terminate the system-event thread.
            }
        }
    }

    private static DisplayTopologySnapshot ReadSnapshot(long version)
    {
        var monitors = new List<DisplayMonitorSnapshot>();
        Exception? callbackException = null;

        MonitorEnumProc callback = (monitor, _, _, _) =>
        {
            try
            {
                var info = new MonitorInfoEx
                {
                    Size = Marshal.SizeOf<MonitorInfoEx>(),
                };

                if (!GetMonitorInfo(monitor, ref info))
                {
                    callbackException = new Win32Exception(
                        Marshal.GetLastWin32Error(),
                        "GetMonitorInfo failed while reading display topology.");
                    return false;
                }

                monitors.Add(new DisplayMonitorSnapshot(
                    DeviceName: info.DeviceName ?? string.Empty,
                    Bounds: ToPhysicalRectangle(info.Monitor),
                    WorkingArea: ToPhysicalRectangle(info.WorkArea),
                    IsPrimary: (info.Flags & MonitorInfoPrimary) != 0));

                return true;
            }
            catch (Exception exception)
            {
                callbackException = exception;
                return false;
            }
        };

        var enumerationCompleted = EnumDisplayMonitors(
            IntPtr.Zero,
            IntPtr.Zero,
            callback,
            IntPtr.Zero);

        if (callbackException is not null)
        {
            throw callbackException;
        }

        if (!enumerationCompleted)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "EnumDisplayMonitors failed while reading display topology.");
        }

        if (monitors.Count == 0)
        {
            throw new DisplayTopologyUnavailableException(
                "Windows did not report any visible display monitors.");
        }

        var ordered = monitors
            .OrderBy(monitor => monitor.Bounds.Top)
            .ThenBy(monitor => monitor.Bounds.Left)
            .ThenBy(monitor => monitor.DeviceName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var virtualBounds = UnionMonitorBounds(ordered);

        return new DisplayTopologySnapshot(
            Version: version,
            CapturedAtUtc: DateTimeOffset.UtcNow,
            VirtualBounds: virtualBounds,
            Monitors: ordered,
            IsRemoteSession: GetSystemMetrics(SmRemoteSession) != 0,
            DpiMode: Application.HighDpiMode.ToString());
    }

    private static PhysicalRectangle UnionMonitorBounds(
        IReadOnlyList<DisplayMonitorSnapshot> monitors)
    {
        var left = monitors.Min(monitor => monitor.Bounds.Left);
        var top = monitors.Min(monitor => monitor.Bounds.Top);
        var right = monitors.Max(monitor => monitor.Bounds.Right);
        var bottom = monitors.Max(monitor => monitor.Bounds.Bottom);

        var width = checked(right - left);
        var height = checked(bottom - top);

        if (width <= 0 || height <= 0)
        {
            throw new DisplayTopologyUnavailableException(
                "Windows reported an invalid virtual desktop rectangle.");
        }

        return new PhysicalRectangle(left, top, width, height);
    }

    private static PhysicalRectangle ToPhysicalRectangle(NativeRectangle rectangle)
    {
        return new PhysicalRectangle(
            rectangle.Left,
            rectangle.Top,
            checked(rectangle.Right - rectangle.Left),
            checked(rectangle.Bottom - rectangle.Top));
    }

    private delegate bool MonitorEnumProc(
        IntPtr monitor,
        IntPtr monitorDeviceContext,
        IntPtr monitorRectangle,
        IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int Size;
        public NativeRectangle Monitor;
        public NativeRectangle WorkArea;
        public int Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string? DeviceName;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(
        IntPtr deviceContext,
        IntPtr clipRectangle,
        MonitorEnumProc callback,
        IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(
        IntPtr monitor,
        ref MonitorInfoEx monitorInfo);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}
