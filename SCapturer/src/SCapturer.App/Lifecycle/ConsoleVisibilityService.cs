using System.Runtime.InteropServices;
using System.Text;

namespace SCapturer.App.Lifecycle;

internal sealed class ConsoleVisibilityService
{
    private const int SwHide = 0;
    private const int SwShow = 5;
    private const int SwRestore = 9;

    private readonly object _gate = new();

    private TextReader? _ownedInput;
    private TextWriter? _ownedOutput;
    private TextWriter? _ownedError;
    private bool _consoleAttached;
    private bool _streamsBound;
    private bool _isVisible;

    public ConsoleVisibilityService()
    {
        _consoleAttached = GetConsoleCP() != 0;
        _streamsBound = _consoleAttached;
        var consoleWindow = GetConsoleWindow();
        _isVisible = _consoleAttached &&
            consoleWindow != IntPtr.Zero &&
            IsWindowVisible(consoleWindow);
    }

    public event Action<bool>? VisibilityChanged;

    public bool IsVisible
    {
        get
        {
            lock (_gate)
            {
                return _isVisible;
            }
        }
    }

    public void ApplyInitialState(bool hidden)
    {
        if (hidden)
        {
            Hide();
        }
    }

    public bool Show(bool activate = true)
    {
        var visibilityChanged = false;

        lock (_gate)
        {
            if (!_consoleAttached)
            {
                if (!AllocConsole())
                {
                    return false;
                }

                _consoleAttached = true;

                try
                {
                    ConfigureConsole();
                    RebindStandardStreams();
                    _streamsBound = true;
                }
                catch
                {
                    DisposeOwnedStreams();
                    _streamsBound = false;
                    _ = FreeConsole();
                    _consoleAttached = false;
                    return false;
                }
            }
            else if (!_streamsBound && GetConsoleWindow() != IntPtr.Zero)
            {
                // A console inherited from a host already has usable standard
                // streams. Rebinding is needed only for consoles allocated here.
                ConfigureConsole();
            }

            var consoleWindow = GetConsoleWindow();
            if (consoleWindow == IntPtr.Zero)
            {
                return false;
            }

            ActivateConsoleWindow(consoleWindow, activate);

            if (!_isVisible)
            {
                _isVisible = true;
                visibilityChanged = true;
            }
        }

        if (visibilityChanged)
        {
            VisibilityChanged?.Invoke(true);
        }

        return true;
    }

    public bool Hide()
    {
        var visibilityChanged = false;

        lock (_gate)
        {
            if (!_consoleAttached || !_isVisible)
            {
                return true;
            }

            TryFlush(Console.Out);
            TryFlush(Console.Error);

            var consoleWindow = GetConsoleWindow();
            if (consoleWindow == IntPtr.Zero)
            {
                return false;
            }

            _ = ShowWindow(consoleWindow, SwHide);
            if (IsWindowVisible(consoleWindow))
            {
                return false;
            }

            _isVisible = false;
            visibilityChanged = true;
        }

        if (visibilityChanged)
        {
            VisibilityChanged?.Invoke(false);
        }

        return true;
    }

    public bool Toggle()
    {
        return IsVisible ? Hide() : Show();
    }

    private static void ConfigureConsole()
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;
        Console.Title = "SCapturer";
    }

    private void RebindStandardStreams()
    {
        DisposeOwnedStreams();

        var inputStream = new FileStream(
            "CONIN$",
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite);
        var outputStream = new FileStream(
            "CONOUT$",
            FileMode.Open,
            FileAccess.Write,
            FileShare.ReadWrite);
        var errorStream = new FileStream(
            "CONOUT$",
            FileMode.Open,
            FileAccess.Write,
            FileShare.ReadWrite);

        var input = new StreamReader(
            inputStream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 1024,
            leaveOpen: false);
        var output = new StreamWriter(
            outputStream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            bufferSize: 1024,
            leaveOpen: false)
        {
            AutoFlush = true,
        };
        var error = new StreamWriter(
            errorStream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            bufferSize: 1024,
            leaveOpen: false)
        {
            AutoFlush = true,
        };

        _ownedInput = input;
        _ownedOutput = output;
        _ownedError = error;

        Console.SetIn(input);
        Console.SetOut(output);
        Console.SetError(error);
    }

    private void DisposeOwnedStreams()
    {
        TryDispose(_ownedInput);
        TryDispose(_ownedOutput);
        TryDispose(_ownedError);
        _ownedInput = null;
        _ownedOutput = null;
        _ownedError = null;
    }

    private static void ActivateConsoleWindow(
        IntPtr consoleWindow,
        bool activate)
    {
        _ = ShowWindow(consoleWindow, SwRestore);
        _ = ShowWindow(consoleWindow, SwShow);

        if (activate)
        {
            _ = SetForegroundWindow(consoleWindow);
        }
    }

    private static void TryFlush(TextWriter writer)
    {
        try
        {
            writer.Flush();
        }
        catch (Exception exception)
            when (exception is IOException or ObjectDisposedException)
        {
            // The console may already be unavailable during teardown.
        }
    }

    private static void TryDispose(IDisposable? disposable)
    {
        try
        {
            disposable?.Dispose();
        }
        catch (Exception exception)
            when (exception is IOException or ObjectDisposedException)
        {
            // Console stream cleanup is best effort.
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeConsole();

    [DllImport("kernel32.dll")]
    private static extern uint GetConsoleCP();

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr windowHandle, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);
}
