using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SCapturer.Core.Models;

namespace SCapturer.Core.Services;

public sealed class HotkeyService : IDisposable
{
    private readonly ManualResetEventSlim _startupCompleted = new(false);
    private Thread? _messageThread;
    private HotkeyWindow? _window;
    private Exception? _startupException;
    private bool _disposed;

    public event Action<long>? FullCaptureRequested;

    public event Action<long>? RegionCaptureRequested;

    public event Action? ExitRequested;

    public event Action? ToggleConsoleRequested;

    public event Action? DisplayConfigurationChanged;

    public void Start(HotkeyBindingSet bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_messageThread is not null)
        {
            return;
        }

        if (!HotkeyBindingService.TryValidateSet(bindings, out var validationError))
        {
            throw new ArgumentException(validationError, nameof(bindings));
        }

        var initialBindings = bindings.CreateSnapshot();

        _messageThread = new Thread(() => RunMessageLoop(initialBindings))
        {
            IsBackground = true,
            Name = "SCapturer Hotkey Message Loop",
        };
        _messageThread.SetApartmentState(ApartmentState.STA);
        _messageThread.Start();

        if (!_startupCompleted.Wait(TimeSpan.FromSeconds(5)))
        {
            throw new TimeoutException("Timed out while starting the global hotkey listener.");
        }

        if (_startupException is not null)
        {
            throw new InvalidOperationException(
                "Could not register the global hotkeys.",
                _startupException);
        }
    }

    public HotkeyRegistrationResult TryReconfigure(HotkeyBindingSet bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!HotkeyBindingService.TryValidateSet(bindings, out var validationError))
        {
            return HotkeyRegistrationResult.Failed(validationError);
        }

        var window = _window;
        if (window is null || !window.IsHandleCreated || window.IsDisposed)
        {
            return HotkeyRegistrationResult.Failed(
                "The hotkey message window is not available.");
        }

        try
        {
            if (window.InvokeRequired)
            {
                return (HotkeyRegistrationResult)window.Invoke(
                    new Func<HotkeyRegistrationResult>(
                        () => window.TryApplyBindings(bindings.CreateSnapshot())));
            }

            return window.TryApplyBindings(bindings.CreateSnapshot());
        }
        catch (ObjectDisposedException)
        {
            return HotkeyRegistrationResult.Failed(
                "The hotkey message window is shutting down.");
        }
        catch (InvalidOperationException exception)
        {
            return HotkeyRegistrationResult.Failed(exception.Message);
        }
        catch (Exception exception)
        {
            return HotkeyRegistrationResult.Failed(
                exception.GetBaseException().Message);
        }
    }

    private void RunMessageLoop(HotkeyBindingSet initialBindings)
    {
        try
        {
            _window = new HotkeyWindow(
                initialBindings,
                onReady: error =>
                {
                    _startupException = error;
                    _startupCompleted.Set();
                },
                onFullCapture: () => FullCaptureRequested?.Invoke(Stopwatch.GetTimestamp()),
                onRegionCapture: () => RegionCaptureRequested?.Invoke(Stopwatch.GetTimestamp()),
                onExit: () => ExitRequested?.Invoke(),
                onToggleConsole: () => ToggleConsoleRequested?.Invoke(),
                onDisplayConfigurationChanged: () => DisplayConfigurationChanged?.Invoke());

            Application.Run(_window);
        }
        catch (Exception exception)
        {
            _startupException = exception;
            _startupCompleted.Set();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        var window = _window;
        if (window is not null && window.IsHandleCreated)
        {
            try
            {
                window.BeginInvoke((Action)window.Close);
            }
            catch (InvalidOperationException)
            {
                // The message loop is already shutting down.
            }
        }

        _messageThread?.Join(TimeSpan.FromSeconds(2));
        _startupCompleted.Dispose();
    }

    private sealed class HotkeyWindow : Form
    {
        private const int WmDisplayChange = 0x007E;
        private const int WmHotkey = 0x0312;
        private const uint ModAlt = 0x0001;
        private const uint ModControl = 0x0002;
        private const uint ModShift = 0x0004;
        private const uint ModWin = 0x0008;
        private const uint ModNoRepeat = 0x4000;
        private const int FullCaptureHotkeyId = 1;
        private const int RegionCaptureHotkeyId = 2;
        private const int ExitHotkeyId = 3;
        private const int ToggleConsoleHotkeyId = 4;

        private readonly HotkeyBindingSet _initialBindings;
        private readonly Action<Exception?> _onReady;
        private readonly Action _onFullCapture;
        private readonly Action _onRegionCapture;
        private readonly Action _onExit;
        private readonly Action _onToggleConsole;
        private readonly Action _onDisplayConfigurationChanged;

        private HotkeyBindingSet? _currentBindings;
        private bool _registeredFullCapture;
        private bool _registeredRegionCapture;
        private bool _registeredExit;
        private bool _registeredToggleConsole;

        public HotkeyWindow(
            HotkeyBindingSet initialBindings,
            Action<Exception?> onReady,
            Action onFullCapture,
            Action onRegionCapture,
            Action onExit,
            Action onToggleConsole,
            Action onDisplayConfigurationChanged)
        {
            _initialBindings = initialBindings.CreateSnapshot();
            _onReady = onReady;
            _onFullCapture = onFullCapture;
            _onRegionCapture = onRegionCapture;
            _onExit = onExit;
            _onToggleConsole = onToggleConsole;
            _onDisplayConfigurationChanged = onDisplayConfigurationChanged;

            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(-32000, -32000);
            Size = new Size(1, 1);
            Opacity = 0;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);

            try
            {
                var result = RegisterBindingSet(_initialBindings);
                if (!result.Success)
                {
                    throw new InvalidOperationException(result.ErrorMessage);
                }

                _currentBindings = _initialBindings.CreateSnapshot();
                Hide();
                _onReady(null);
            }
            catch (Exception exception)
            {
                _onReady(exception);
                Close();
            }
        }

        public HotkeyRegistrationResult TryApplyBindings(
            HotkeyBindingSet bindings)
        {
            if (!HotkeyBindingService.TryValidateSet(bindings, out var validationError))
            {
                return HotkeyRegistrationResult.Failed(validationError);
            }

            var previous = _currentBindings?.CreateSnapshot();
            UnregisterAll();

            var registration = RegisterBindingSet(bindings);
            if (registration.Success)
            {
                _currentBindings = bindings.CreateSnapshot();
                return registration;
            }

            UnregisterAll();

            if (previous is not null)
            {
                var rollback = RegisterBindingSet(previous);
                if (rollback.Success)
                {
                    _currentBindings = previous;
                }
                else
                {
                    _currentBindings = null;
                    return HotkeyRegistrationResult.Failed(
                        $"{registration.ErrorMessage} The previous hotkeys also could not be restored: " +
                        rollback.ErrorMessage);
                }
            }

            return registration;
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == WmDisplayChange)
            {
                _onDisplayConfigurationChanged();
            }

            if (message.Msg == WmHotkey)
            {
                switch (message.WParam.ToInt32())
                {
                    case FullCaptureHotkeyId:
                        _onFullCapture();
                        return;
                    case RegionCaptureHotkeyId:
                        _onRegionCapture();
                        return;
                    case ExitHotkeyId:
                        _onExit();
                        return;
                    case ToggleConsoleHotkeyId:
                        _onToggleConsole();
                        return;
                }
            }

            base.WndProc(ref message);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            UnregisterAll();
            base.OnFormClosed(e);
        }

        private HotkeyRegistrationResult RegisterBindingSet(
            HotkeyBindingSet bindings)
        {
            var fullResult = RegisterOne(
                FullCaptureHotkeyId,
                bindings.FullCapture,
                "full capture",
                out _registeredFullCapture);

            if (!fullResult.Success)
            {
                return fullResult;
            }

            var regionResult = RegisterOne(
                RegionCaptureHotkeyId,
                bindings.RegionCapture,
                "region capture",
                out _registeredRegionCapture);

            if (!regionResult.Success)
            {
                return regionResult;
            }

            var exitResult = RegisterOne(
                ExitHotkeyId,
                bindings.Exit,
                "exit",
                out _registeredExit);

            if (!exitResult.Success)
            {
                return exitResult;
            }

            return RegisterOne(
                ToggleConsoleHotkeyId,
                bindings.ToggleConsole,
                "toggle console",
                out _registeredToggleConsole);
        }

        private HotkeyRegistrationResult RegisterOne(
            int identifier,
            HotkeyBinding binding,
            string actionName,
            out bool registered)
        {
            registered = RegisterHotKey(
                Handle,
                identifier,
                CreateNativeModifiers(binding),
                (uint)(binding.VirtualKey & (int)Keys.KeyCode));

            if (registered)
            {
                return HotkeyRegistrationResult.Succeeded;
            }

            var errorCode = Marshal.GetLastWin32Error();
            var errorText = new Win32Exception(errorCode).Message;

            return HotkeyRegistrationResult.Failed(
                $"{HotkeyBindingService.Format(binding)} is unavailable for {actionName}. " +
                $"Windows error {errorCode}: {errorText}");
        }

        private void UnregisterAll()
        {
            if (_registeredFullCapture)
            {
                UnregisterHotKey(Handle, FullCaptureHotkeyId);
                _registeredFullCapture = false;
            }

            if (_registeredRegionCapture)
            {
                UnregisterHotKey(Handle, RegionCaptureHotkeyId);
                _registeredRegionCapture = false;
            }

            if (_registeredExit)
            {
                UnregisterHotKey(Handle, ExitHotkeyId);
                _registeredExit = false;
            }

            if (_registeredToggleConsole)
            {
                UnregisterHotKey(Handle, ToggleConsoleHotkeyId);
                _registeredToggleConsole = false;
            }
        }

        private static uint CreateNativeModifiers(HotkeyBinding binding)
        {
            var modifiers = ModNoRepeat;

            if (binding.Control)
            {
                modifiers |= ModControl;
            }

            if (binding.Shift)
            {
                modifiers |= ModShift;
            }

            if (binding.Alt)
            {
                modifiers |= ModAlt;
            }

            if (binding.Windows)
            {
                modifiers |= ModWin;
            }

            return modifiers;
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RegisterHotKey(
            IntPtr windowHandle,
            int id,
            uint modifiers,
            uint virtualKey);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnregisterHotKey(IntPtr windowHandle, int id);
    }
}
