using System.Drawing;

namespace SCapturer.Core.Display;

public readonly record struct PhysicalRectangle(
    int X,
    int Y,
    int Width,
    int Height)
{
    public int Left => X;

    public int Top => Y;

    public int Right => checked(X + Width);

    public int Bottom => checked(Y + Height);

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public Rectangle ToRectangle() => new(X, Y, Width, Height);

    public static PhysicalRectangle FromRectangle(Rectangle rectangle)
    {
        return new PhysicalRectangle(
            rectangle.X,
            rectangle.Y,
            rectangle.Width,
            rectangle.Height);
    }
}

public sealed record DisplayMonitorSnapshot(
    string DeviceName,
    PhysicalRectangle Bounds,
    PhysicalRectangle WorkingArea,
    bool IsPrimary);

public sealed record DisplayTopologySnapshot(
    long Version,
    DateTimeOffset CapturedAtUtc,
    PhysicalRectangle VirtualBounds,
    IReadOnlyList<DisplayMonitorSnapshot> Monitors,
    bool IsRemoteSession,
    string DpiMode)
{
    public int MonitorCount => Monitors.Count;

    public bool StructurallyEquals(DisplayTopologySnapshot other)
    {
        ArgumentNullException.ThrowIfNull(other);

        if (VirtualBounds != other.VirtualBounds ||
            IsRemoteSession != other.IsRemoteSession ||
            Monitors.Count != other.Monitors.Count)
        {
            return false;
        }

        for (var index = 0; index < Monitors.Count; index++)
        {
            var left = Monitors[index];
            var right = other.Monitors[index];

            if (!string.Equals(
                    left.DeviceName,
                    right.DeviceName,
                    StringComparison.OrdinalIgnoreCase) ||
                left.Bounds != right.Bounds ||
                left.WorkingArea != right.WorkingArea ||
                left.IsPrimary != right.IsPrimary)
            {
                return false;
            }
        }

        return true;
    }
}

public sealed record DisplayTopologyChange(
    long Version,
    string Reason,
    bool IsStable,
    DisplayTopologySnapshot Snapshot);

public sealed class DisplayTopologyUnavailableException : InvalidOperationException
{
    public DisplayTopologyUnavailableException(string message)
        : base(message)
    {
    }
}

public sealed class DisplayTopologyChangedException : InvalidOperationException
{
    public DisplayTopologyChangedException(string message)
        : base(message)
    {
    }
}
