namespace SCapturer.Core.Capture;

public sealed class CaptureBackendProvider
{
    private readonly ICaptureBackend _referenceBackend;
    private readonly ICaptureBackend _nativeBackend;

    public CaptureBackendProvider()
        : this(
            new ReferenceGdiPlusCaptureBackend(),
            new NativeGdiWicCaptureBackend())
    {
    }

    internal CaptureBackendProvider(
        ICaptureBackend referenceBackend,
        ICaptureBackend nativeBackend)
    {
        _referenceBackend = referenceBackend;
        _nativeBackend = nativeBackend;
    }

    public CaptureBackendSelection GetSelection(CaptureBackendMode mode)
    {
        return Resolve(mode).Selection;
    }

    public bool IsNativeAvailable(out string? reason)
    {
        return _nativeBackend.IsAvailable(out reason);
    }

    public ICaptureBackend GetBackend(CaptureBackendMode mode)
    {
        return Resolve(mode).Backend;
    }

    public ICaptureBackend GetBackend(CaptureBackendKind kind)
    {
        return kind switch
        {
            CaptureBackendKind.ReferenceGdiPlus => _referenceBackend,
            CaptureBackendKind.NativeGdiWic => GetNativeOrThrow(),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
    }

    private ResolvedBackend Resolve(CaptureBackendMode mode)
    {
        return mode switch
        {
            CaptureBackendMode.ReferenceGdiPlus => CreateResolved(
                mode,
                _referenceBackend,
                isFallback: false,
                fallbackReason: null),

            CaptureBackendMode.NativeGdiWic => ResolveNative(mode),

            CaptureBackendMode.Auto => ResolveAuto(),

            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
        };
    }

    private ResolvedBackend ResolveNative(CaptureBackendMode requestedMode)
    {
        if (_nativeBackend.IsAvailable(out var reason))
        {
            return CreateResolved(
                requestedMode,
                _nativeBackend,
                isFallback: false,
                fallbackReason: null);
        }

        return CreateResolved(
            requestedMode,
            _referenceBackend,
            isFallback: true,
            fallbackReason: reason ?? "The native backend is unavailable.");
    }

    private ResolvedBackend ResolveAuto()
    {
        if (_nativeBackend.IsAvailable(out var reason))
        {
            return CreateResolved(
                CaptureBackendMode.Auto,
                _nativeBackend,
                isFallback: false,
                fallbackReason: null);
        }

        return CreateResolved(
            CaptureBackendMode.Auto,
            _referenceBackend,
            isFallback: true,
            fallbackReason: reason ?? "The native backend is unavailable.");
    }

    private ICaptureBackend GetNativeOrThrow()
    {
        if (_nativeBackend.IsAvailable(out var reason))
        {
            return _nativeBackend;
        }

        throw new InvalidOperationException(
            reason ?? "The native GDI + WIC backend is unavailable.");
    }

    private static ResolvedBackend CreateResolved(
        CaptureBackendMode requestedMode,
        ICaptureBackend backend,
        bool isFallback,
        string? fallbackReason)
    {
        return new ResolvedBackend(
            backend,
            new CaptureBackendSelection(
                requestedMode,
                backend.Kind,
                backend.Name,
                isFallback,
                fallbackReason));
    }

    private sealed record ResolvedBackend(
        ICaptureBackend Backend,
        CaptureBackendSelection Selection);
}
