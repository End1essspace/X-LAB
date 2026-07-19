using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SCapturer.Reliability;

internal static class ProcessResourceSampler
{
    private const uint GrGdiObjects = 0;
    private const uint GrUserObjects = 1;

    public static ResourceSample Capture(
        Process process,
        string phase,
        int iteration)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentException.ThrowIfNullOrWhiteSpace(phase);

        process.Refresh();

        if (process.HasExited)
        {
            throw new InvalidOperationException(
                "The SCapturer process exited before resource sampling.");
        }

        var processHandle = process.Handle;
        var gdi = GetGuiResources(processHandle, GrGdiObjects);
        var user = GetGuiResources(processHandle, GrUserObjects);

        if (gdi == 0 && Marshal.GetLastWin32Error() != 0)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "GetGuiResources failed for GDI objects.");
        }

        return new ResourceSample(
            DateTimeOffset.UtcNow,
            phase,
            iteration,
            process.Id,
            process.WorkingSet64,
            process.PrivateMemorySize64,
            process.HandleCount,
            process.Threads.Count,
            gdi,
            user);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetGuiResources(
        IntPtr processHandle,
        uint flags);
}
