using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using SCapturer.Core.Display;

namespace SCapturer.Core.Capture;

internal sealed class NativeGdiWicCaptureBackend : ICaptureBackend
{
    private const uint SourceCopy = 0x00CC0020;

    private readonly object _availabilityGate = new();
    private bool? _available;
    private string? _unavailableReason;

    public CaptureBackendKind Kind => CaptureBackendKind.NativeGdiWic;

    public string Name => "Native GDI + WIC";

    public bool IsAvailable(out string? reason)
    {
        lock (_availabilityGate)
        {
            if (_available is null)
            {
                try
                {
                    WicPngEncoder.Probe();
                    _available = true;
                    _unavailableReason = null;
                }
                catch (Exception exception)
                {
                    _available = false;
                    _unavailableReason =
                        "Windows Imaging Component initialization failed: " +
                        exception.GetBaseException().Message;
                }
            }

            reason = _unavailableReason;
            return _available.Value;
        }
    }

    public CaptureBackendCaptureResult Capture(
        PhysicalRectangle bounds,
        Action<CaptureBackendPhase>? phaseChanged = null)
    {
        if (bounds.IsEmpty)
        {
            throw new ArgumentException("Capture bounds cannot be empty.", nameof(bounds));
        }

        var screenDeviceContext = GetDC(IntPtr.Zero);
        if (screenDeviceContext == IntPtr.Zero)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "GetDC failed while opening the desktop device context.");
        }

        NativeGdiCaptureFrame? frame = null;

        try
        {
            phaseChanged?.Invoke(CaptureBackendPhase.BufferAllocation);
            var stageStarted = Stopwatch.GetTimestamp();
            frame = NativeGdiCaptureFrame.Create(
                screenDeviceContext,
                bounds.Width,
                bounds.Height);
            var allocationMilliseconds = ElapsedMilliseconds(stageStarted);

            phaseChanged?.Invoke(CaptureBackendPhase.PixelAcquisition);
            stageStarted = Stopwatch.GetTimestamp();

            if (!BitBlt(
                    frame.MemoryDeviceContext,
                    0,
                    0,
                    bounds.Width,
                    bounds.Height,
                    screenDeviceContext,
                    bounds.Left,
                    bounds.Top,
                    SourceCopy))
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "BitBlt failed while acquiring the physical desktop pixels.");
            }

            frame.ForceOpaqueAlpha();
            var acquisitionMilliseconds = ElapsedMilliseconds(stageStarted);

            var completed = frame;
            frame = null;

            return new CaptureBackendCaptureResult(
                completed,
                allocationMilliseconds,
                acquisitionMilliseconds);
        }
        finally
        {
            frame?.Dispose();
            _ = ReleaseDC(IntPtr.Zero, screenDeviceContext);
        }
    }

    public CaptureFrame Crop(CaptureFrame source, Rectangle region)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (source is not NativeGdiCaptureFrame nativeSource)
        {
            throw new ArgumentException(
                "The native backend can crop only native capture frames.",
                nameof(source));
        }

        ValidateRegion(source, region);

        var cropped = NativeGdiCaptureFrame.Create(
            nativeSource.MemoryDeviceContext,
            region.Width,
            region.Height);

        try
        {
            if (!BitBlt(
                    cropped.MemoryDeviceContext,
                    0,
                    0,
                    region.Width,
                    region.Height,
                    nativeSource.MemoryDeviceContext,
                    region.X,
                    region.Y,
                    SourceCopy))
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "BitBlt failed while cropping the native capture frame.");
            }

            cropped.ForceOpaqueAlpha();
            return cropped;
        }
        catch
        {
            cropped.Dispose();
            throw;
        }
    }

    public void SavePng(CaptureFrame frame, string filePath)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (frame is not NativeGdiCaptureFrame nativeFrame)
        {
            throw new ArgumentException(
                "The WIC encoder requires a native GDI frame.",
                nameof(frame));
        }

        WicPngEncoder.Save(nativeFrame, filePath);
    }

    private static void ValidateRegion(CaptureFrame source, Rectangle region)
    {
        var frameBounds = new Rectangle(0, 0, source.Width, source.Height);
        if (region.Width <= 0 ||
            region.Height <= 0 ||
            Rectangle.Intersect(frameBounds, region) != region)
        {
            throw new ArgumentOutOfRangeException(
                nameof(region),
                region,
                "The crop rectangle must be fully contained in the source frame.");
        }
    }

    private static double ElapsedMilliseconds(long startedTimestamp)
    {
        return Stopwatch.GetElapsedTime(startedTimestamp).TotalMilliseconds;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetDC(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(
        IntPtr windowHandle,
        IntPtr deviceContext);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BitBlt(
        IntPtr destinationDeviceContext,
        int destinationX,
        int destinationY,
        int width,
        int height,
        IntPtr sourceDeviceContext,
        int sourceX,
        int sourceY,
        uint rasterOperation);
}

internal sealed class NativeGdiCaptureFrame : CaptureFrame
{
    private const uint DibRgbColors = 0;
    private const uint BiRgb = 0;
    private static readonly IntPtr HgdiError = new(-1);

    private IntPtr _memoryDeviceContext;
    private IntPtr _bitmapHandle;
    private IntPtr _previousObject;
    private IntPtr _pixelBuffer;
    private Bitmap? _bitmapView;
    private int _disposed;

    private NativeGdiCaptureFrame(
        int width,
        int height,
        int stride,
        IntPtr memoryDeviceContext,
        IntPtr bitmapHandle,
        IntPtr previousObject,
        IntPtr pixelBuffer,
        Bitmap bitmapView)
    {
        Width = width;
        Height = height;
        Stride = stride;
        _memoryDeviceContext = memoryDeviceContext;
        _bitmapHandle = bitmapHandle;
        _previousObject = previousObject;
        _pixelBuffer = pixelBuffer;
        _bitmapView = bitmapView;
    }

    public override int Width { get; }

    public override int Height { get; }

    public override int Stride { get; }

    public IntPtr MemoryDeviceContext => GetHandle(_memoryDeviceContext);

    public IntPtr PixelBuffer => GetHandle(_pixelBuffer);

    public int BufferSizeBytes => checked(Stride * Height);

    public override Bitmap Bitmap => _bitmapView ??
        throw new ObjectDisposedException(nameof(NativeGdiCaptureFrame));

    public override CaptureBackendKind BackendKind =>
        CaptureBackendKind.NativeGdiWic;

    public override string BackendName => "Native GDI + WIC";

    public static NativeGdiCaptureFrame Create(
        IntPtr compatibleDeviceContext,
        int width,
        int height)
    {
        if (compatibleDeviceContext == IntPtr.Zero)
        {
            throw new ArgumentException(
                "A compatible device context is required.",
                nameof(compatibleDeviceContext));
        }

        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                "Frame dimensions must be positive.");
        }

        var memoryDeviceContext = CreateCompatibleDC(compatibleDeviceContext);
        if (memoryDeviceContext == IntPtr.Zero)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "CreateCompatibleDC failed for the native capture frame.");
        }

        IntPtr bitmapHandle = IntPtr.Zero;
        IntPtr previousObject = IntPtr.Zero;
        IntPtr pixelBuffer = IntPtr.Zero;
        Bitmap? bitmapView = null;

        try
        {
            var bitmapInfo = new BitmapInfo
            {
                Header = new BitmapInfoHeader
                {
                    Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                    Width = width,
                    Height = -height,
                    Planes = 1,
                    BitCount = 32,
                    Compression = BiRgb,
                },
            };

            bitmapHandle = CreateDIBSection(
                compatibleDeviceContext,
                ref bitmapInfo,
                DibRgbColors,
                out pixelBuffer,
                IntPtr.Zero,
                0);

            if (bitmapHandle == IntPtr.Zero || pixelBuffer == IntPtr.Zero)
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "CreateDIBSection failed for the native capture buffer.");
            }

            previousObject = SelectObject(memoryDeviceContext, bitmapHandle);
            if (previousObject == IntPtr.Zero || previousObject == HgdiError)
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "SelectObject failed for the native capture buffer.");
            }

            var stride = checked(width * 4);
            bitmapView = new Bitmap(
                width,
                height,
                stride,
                PixelFormat.Format32bppArgb,
                pixelBuffer);

            return new NativeGdiCaptureFrame(
                width,
                height,
                stride,
                memoryDeviceContext,
                bitmapHandle,
                previousObject,
                pixelBuffer,
                bitmapView);
        }
        catch
        {
            bitmapView?.Dispose();

            if (previousObject != IntPtr.Zero && previousObject != HgdiError)
            {
                _ = SelectObject(memoryDeviceContext, previousObject);
            }

            if (bitmapHandle != IntPtr.Zero)
            {
                _ = DeleteObject(bitmapHandle);
            }

            _ = DeleteDC(memoryDeviceContext);
            throw;
        }
    }

    public unsafe void ForceOpaqueAlpha()
    {
        var buffer = PixelBuffer;
        var pixels = new Span<uint>(
            buffer.ToPointer(),
            checked(Width * Height));

        for (var index = 0; index < pixels.Length; index++)
        {
            pixels[index] |= 0xFF000000u;
        }
    }

    public override void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _bitmapView?.Dispose();
        _bitmapView = null;

        if (_memoryDeviceContext != IntPtr.Zero &&
            _previousObject != IntPtr.Zero &&
            _previousObject != HgdiError)
        {
            _ = SelectObject(_memoryDeviceContext, _previousObject);
        }

        if (_bitmapHandle != IntPtr.Zero)
        {
            _ = DeleteObject(_bitmapHandle);
        }

        if (_memoryDeviceContext != IntPtr.Zero)
        {
            _ = DeleteDC(_memoryDeviceContext);
        }

        _pixelBuffer = IntPtr.Zero;
        _previousObject = IntPtr.Zero;
        _bitmapHandle = IntPtr.Zero;
        _memoryDeviceContext = IntPtr.Zero;
    }

    private IntPtr GetHandle(IntPtr value)
    {
        if (Volatile.Read(ref _disposed) != 0 || value == IntPtr.Zero)
        {
            throw new ObjectDisposedException(nameof(NativeGdiCaptureFrame));
        }

        return value;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ColorsUsed;
        public uint ColorsImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RgbQuad
    {
        public byte Blue;
        public byte Green;
        public byte Red;
        public byte Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader Header;
        public RgbQuad Colors;
    }

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleDC(IntPtr deviceContext);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateDIBSection(
        IntPtr deviceContext,
        ref BitmapInfo bitmapInfo,
        uint usage,
        out IntPtr bits,
        IntPtr section,
        uint offset);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr SelectObject(
        IntPtr deviceContext,
        IntPtr graphicsObject);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr graphicsObject);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr deviceContext);
}
