using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace SCapturer.Core.Capture;

internal static class WicPngEncoder
{
    private const uint StgmWrite = 0x00000001;
    private const uint StgmShareDenyWrite = 0x00000020;
    private const uint StgmCreate = 0x00001000;
    private const int WicBitmapEncoderNoCache = 2;

    private static readonly Guid ClsidWicPngEncoder =
        new("27949969-876A-41D7-9447-568F6A35A4DC");

    private static readonly Guid PixelFormat32BppBgra =
        new("6FDDC324-4E03-4BFE-B185-3D77768DC90F");

    public static void Probe()
    {
        var encoder = CreateEncoder();
        ReleaseComObject(encoder);
    }

    public static void Save(
        NativeGdiCaptureFrame frame,
        string filePath)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        IStream? stream = null;
        IWICBitmapEncoder? encoder = null;
        IWICBitmapFrameEncode? frameEncoder = null;
        IntPtr encoderOptions = IntPtr.Zero;

        try
        {
            var streamPointer = IntPtr.Zero;
            ThrowIfFailed(SHCreateStreamOnFileEx(
                filePath,
                StgmWrite | StgmShareDenyWrite | StgmCreate,
                fileAttributes: 0,
                create: true,
                templateStream: IntPtr.Zero,
                stream: out streamPointer));

            try
            {
                stream = (IStream)Marshal.GetObjectForIUnknown(streamPointer);
            }
            finally
            {
                if (streamPointer != IntPtr.Zero)
                {
                    _ = Marshal.Release(streamPointer);
                }
            }

            encoder = CreateEncoder();
            ThrowIfFailed(encoder.Initialize(stream, WicBitmapEncoderNoCache));
            ThrowIfFailed(encoder.CreateNewFrame(out frameEncoder, out encoderOptions));
            ThrowIfFailed(frameEncoder.Initialize(encoderOptions));
            ThrowIfFailed(frameEncoder.SetSize((uint)frame.Width, (uint)frame.Height));
            ThrowIfFailed(frameEncoder.SetResolution(96, 96));

            var pixelFormat = PixelFormat32BppBgra;
            ThrowIfFailed(frameEncoder.SetPixelFormat(ref pixelFormat));

            if (pixelFormat != PixelFormat32BppBgra)
            {
                throw new NotSupportedException(
                    "The Windows PNG encoder did not accept 32-bit BGRA pixels.");
            }

            ThrowIfFailed(frameEncoder.WritePixels(
                (uint)frame.Height,
                (uint)frame.Stride,
                checked((uint)frame.BufferSizeBytes),
                frame.PixelBuffer));

            ThrowIfFailed(frameEncoder.Commit());
            ThrowIfFailed(encoder.Commit());
        }
        catch
        {
            TryDelete(filePath);
            throw;
        }
        finally
        {
            if (encoderOptions != IntPtr.Zero)
            {
                _ = Marshal.Release(encoderOptions);
            }

            ReleaseComObject(frameEncoder);
            ReleaseComObject(encoder);
            ReleaseComObject(stream);
        }
    }

    private static IWICBitmapEncoder CreateEncoder()
    {
        var encoderType = Type.GetTypeFromCLSID(
            ClsidWicPngEncoder,
            throwOnError: true)
            ?? throw new InvalidOperationException(
                "Windows did not expose the WIC PNG encoder class.");

        var instance = Activator.CreateInstance(encoderType)
            ?? throw new InvalidOperationException(
                "Windows could not create the WIC PNG encoder.");

        return (IWICBitmapEncoder)instance;
    }

    private static void ThrowIfFailed(int hresult)
    {
        if (hresult < 0)
        {
            Marshal.ThrowExceptionForHR(hresult);
        }
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            _ = Marshal.FinalReleaseComObject(value);
        }
    }

    private static void TryDelete(string filePath)
    {
        try
        {
            File.Delete(filePath);
        }
        catch (IOException)
        {
            // The original encoder exception is more important.
        }
        catch (UnauthorizedAccessException)
        {
            // The original encoder exception is more important.
        }
    }

    [ComImport]
    [Guid("00000103-A8F2-4877-BA0A-FD2B6645FB94")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IWICBitmapEncoder
    {
        [PreserveSig]
        int Initialize(
            [MarshalAs(UnmanagedType.Interface)] IStream stream,
            int cacheOption);

        [PreserveSig]
        int GetContainerFormat(out Guid containerFormat);

        [PreserveSig]
        int GetEncoderInfo(out IntPtr encoderInfo);

        [PreserveSig]
        int SetColorContexts(uint count, IntPtr colorContexts);

        [PreserveSig]
        int SetPalette(IntPtr palette);

        [PreserveSig]
        int SetThumbnail(IntPtr thumbnail);

        [PreserveSig]
        int SetPreview(IntPtr preview);

        [PreserveSig]
        int CreateNewFrame(
            [MarshalAs(UnmanagedType.Interface)] out IWICBitmapFrameEncode frame,
            out IntPtr encoderOptions);

        [PreserveSig]
        int Commit();

        [PreserveSig]
        int GetMetadataQueryWriter(out IntPtr metadataQueryWriter);
    }

    [ComImport]
    [Guid("00000105-A8F2-4877-BA0A-FD2B6645FB94")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IWICBitmapFrameEncode
    {
        [PreserveSig]
        int Initialize(IntPtr encoderOptions);

        [PreserveSig]
        int SetSize(uint width, uint height);

        [PreserveSig]
        int SetResolution(double dpiX, double dpiY);

        [PreserveSig]
        int SetPixelFormat(ref Guid pixelFormat);

        [PreserveSig]
        int SetColorContexts(uint count, IntPtr colorContexts);

        [PreserveSig]
        int SetPalette(IntPtr palette);

        [PreserveSig]
        int SetThumbnail(IntPtr thumbnail);

        [PreserveSig]
        int WritePixels(
            uint lineCount,
            uint stride,
            uint bufferSize,
            IntPtr pixels);

        [PreserveSig]
        int WriteSource(IntPtr bitmapSource, IntPtr sourceRectangle);

        [PreserveSig]
        int Commit();

        [PreserveSig]
        int GetMetadataQueryWriter(out IntPtr metadataQueryWriter);
    }

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateStreamOnFileEx(
        [MarshalAs(UnmanagedType.LPWStr)] string fileName,
        uint mode,
        uint fileAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool create,
        IntPtr templateStream,
        out IntPtr stream);
}
