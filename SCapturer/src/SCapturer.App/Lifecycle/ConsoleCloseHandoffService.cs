using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SCapturer.App.Lifecycle;

internal sealed class ConsoleCloseHandoffService : IDisposable
{
    private const uint CtrlCloseEvent = 2;
    private static readonly TimeSpan ResumeWaitTimeout = TimeSpan.FromSeconds(15);

    private readonly ConsoleVisibilityService _consoleVisibility;
    private readonly ConsoleControlHandler _handler;
    private int _registered;
    private int _handoffStarted;
    private int _disposed;

    public ConsoleCloseHandoffService(
        ConsoleVisibilityService consoleVisibility)
    {
        _consoleVisibility = consoleVisibility;
        _handler = HandleConsoleControl;
        _consoleVisibility.ConsoleAttached += OnConsoleAttached;

        if (_consoleVisibility.IsAttached)
        {
            RegisterHandler();
        }
    }

    public static void WaitForPreviousProcess(int processId)
    {
        if (processId <= 0 || processId == Environment.ProcessId)
        {
            return;
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            process.WaitForExit((int)ResumeWaitTimeout.TotalMilliseconds);
        }
        catch (ArgumentException)
        {
            // The previous process has already exited.
        }
        catch (InvalidOperationException)
        {
            // The previous process exited between lookup and wait.
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        _consoleVisibility.ConsoleAttached -= OnConsoleAttached;

        if (Interlocked.Exchange(ref _registered, 0) == 1)
        {
            _ = SetConsoleCtrlHandler(_handler, add: false);
        }
    }

    private void OnConsoleAttached()
    {
        RegisterHandler();
    }

    private void RegisterHandler()
    {
        if (Volatile.Read(ref _disposed) == 1 ||
            Interlocked.CompareExchange(ref _registered, 1, 0) != 0)
        {
            return;
        }

        if (!SetConsoleCtrlHandler(_handler, add: true))
        {
            Volatile.Write(ref _registered, 0);
        }
    }

    private bool HandleConsoleControl(uint controlType)
    {
        if (controlType != CtrlCloseEvent)
        {
            return false;
        }

        if (Interlocked.Exchange(ref _handoffStarted, 1) == 0)
        {
            TryStartHiddenReplacement();
        }

        // Windows terminates a process after CTRL_CLOSE_EVENT even when a
        // handler returns TRUE. The replacement waits for this PID to exit,
        // then acquires the normal single-instance mutex and resumes hidden.
        return true;
    }

    private static void TryStartHiddenReplacement()
    {
        try
        {
            var executablePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executablePath) ||
                !File.Exists(executablePath))
            {
                return;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };

            var managedEntryPath = ResolveManagedEntryPath(executablePath);
            if (managedEntryPath is not null)
            {
                startInfo.ArgumentList.Add(managedEntryPath);
            }

            startInfo.ArgumentList.Add(
                $"--resume-background={Environment.ProcessId}");

            _ = Process.Start(startInfo);
        }
        catch
        {
            // The control handler must remain minimal and cannot safely report
            // failures through the console while Windows is tearing it down.
        }
    }


    private static string? ResolveManagedEntryPath(string executablePath)
    {
        if (!IsDotnetHost(executablePath))
        {
            return null;
        }

        var candidate = Path.Combine(
            AppContext.BaseDirectory,
            "SCapturer.dll");

        return File.Exists(candidate)
            ? candidate
            : null;
    }

    private static bool IsDotnetHost(string executablePath)
    {
        return string.Equals(
            Path.GetFileNameWithoutExtension(executablePath),
            "dotnet",
            StringComparison.OrdinalIgnoreCase);
    }

    private delegate bool ConsoleControlHandler(uint controlType);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetConsoleCtrlHandler(
        ConsoleControlHandler handler,
        [MarshalAs(UnmanagedType.Bool)] bool add);
}
