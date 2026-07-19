using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SCapturer.Core.Services;

public sealed class HotkeyService : IDisposable
{
    private readonly ManualResetEventSlim _startupCompleted = new(false);
    private Thread? _messageThread;
    private HotkeyWindow? _window;
    private Exception? _startupException;
    private bool _disposed;

    public event Action? FullCaptureRequested;

    public event Action? ExitRequested;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_messageThread is not null)
        {
            return;
        }

        _messageThread = new Thread(RunMessageLoop)
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

    private void RunMessageLoop()
    {
        try
        {
            _window = new HotkeyWindow(
                onReady: error =>
                {
                    _startupException = error;
                    _startupCompleted.Set();
                },
                onFullCapture: () => FullCaptureRequested?.Invoke(),
                onExit: () => ExitRequested?.Invoke());

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
        private const int WmHotkey = 0x0312;
        private const uint ModControl = 0x0002;
        private const uint ModShift = 0x0004;
        private const int FullCaptureHotkeyId = 1;
        private const int ExitHotkeyId = 2;

        private readonly Action<Exception?> _onReady;
        private readonly Action _onFullCapture;
        private readonly Action _onExit;
        private bool _registeredFullCapture;
        private bool _registeredExit;

        public HotkeyWindow(
            Action<Exception?> onReady,
            Action onFullCapture,
            Action onExit)
        {
            _onReady = onReady;
            _onFullCapture = onFullCapture;
            _onExit = onExit;

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
                _registeredFullCapture = RegisterHotKey(
                    Handle,
                    FullCaptureHotkeyId,
                    ModControl | ModShift,
                    (uint)Keys.G);

                if (!_registeredFullCapture)
                {
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error(),
                        "Ctrl+Shift+G is unavailable.");
                }

                _registeredExit = RegisterHotKey(
                    Handle,
                    ExitHotkeyId,
                    ModControl | ModShift,
                    (uint)Keys.Q);

                if (!_registeredExit)
                {
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error(),
                        "Ctrl+Shift+Q is unavailable.");
                }

                Hide();
                _onReady(null);
            }
            catch (Exception exception)
            {
                _onReady(exception);
                Close();
            }
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == WmHotkey)
            {
                switch (message.WParam.ToInt32())
                {
                    case FullCaptureHotkeyId:
                        _onFullCapture();
                        return;
                    case ExitHotkeyId:
                        _onExit();
                        return;
                }
            }

            base.WndProc(ref message);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (_registeredFullCapture)
            {
                UnregisterHotKey(Handle, FullCaptureHotkeyId);
            }

            if (_registeredExit)
            {
                UnregisterHotKey(Handle, ExitHotkeyId);
            }

            base.OnFormClosed(e);
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
