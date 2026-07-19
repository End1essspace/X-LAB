using System.Linq;
using System.Threading.Channels;
using SCapturer.Core.Models;
using SCapturer.Core.Services;
using SCapturer.Core.Snipping;

namespace SCapturer.Core.Pipeline;

public sealed class CaptureCoordinator : IDisposable
{
    private readonly CaptureService _captureService;
    private readonly SnippingService _snippingService;
    private readonly Channel<byte> _signals;
    private readonly ManualResetEventSlim _workerStarted = new(false);
    private readonly object _gate = new();

    private Thread? _workerThread;
    private CaptureRequest? _pendingRequest;
    private CapturePipelineSnapshot _snapshot = CapturePipelineSnapshot.Initial;
    private Exception? _startupException;
    private bool _acceptingRequests;
    private bool _active;
    private bool _disposed;
    private long _nextRequestId;
    private long _snapshotVersion;
    private CaptureKind? _activeKind;
    private string? _activeTrigger;

    public CaptureCoordinator(
        CaptureService captureService,
        SnippingService snippingService)
    {
        _captureService = captureService;
        _snippingService = snippingService;

        _signals = Channel.CreateBounded<byte>(new BoundedChannelOptions(1)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false,
        });
    }

    public event Action<CapturePipelineSnapshot>? StateChanged;

    public event Action<CaptureCompletedEvent>? CaptureCompleted;

    public event Action<CaptureCancelledEvent>? CaptureCancelled;

    public event Action<CaptureFailedEvent>? CaptureFailed;

    public CapturePipelineSnapshot Snapshot
    {
        get
        {
            lock (_gate)
            {
                return _snapshot;
            }
        }
    }

    public bool HasWork => Snapshot.HasWork;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_gate)
        {
            if (_workerThread is not null)
            {
                return;
            }

            _acceptingRequests = true;
            _workerThread = new Thread(RunWorker)
            {
                IsBackground = true,
                Name = "SCapturer Capture Worker",
            };
            _workerThread.SetApartmentState(ApartmentState.STA);
            _workerThread.Start();
        }

        if (!_workerStarted.Wait(TimeSpan.FromSeconds(5)))
        {
            throw new TimeoutException("Timed out while starting the capture worker.");
        }

        if (_startupException is not null)
        {
            throw new InvalidOperationException(
                "The capture worker could not start.",
                _startupException);
        }
    }

    public CaptureEnqueueResult TryEnqueue(
        CaptureKind kind,
        AppSettings settings,
        long requestTimestamp,
        string trigger)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(trigger);
        ObjectDisposedException.ThrowIf(_disposed, this);

        CapturePipelineSnapshot snapshot;
        CaptureEnqueueResult result;

        lock (_gate)
        {
            if (_workerThread is null)
            {
                throw new InvalidOperationException("The capture coordinator has not been started.");
            }

            if (!_acceptingRequests)
            {
                return CaptureEnqueueResult.Stopping;
            }

            result = _pendingRequest is null
                ? CaptureEnqueueResult.Accepted
                : CaptureEnqueueResult.Coalesced;

            _pendingRequest = new CaptureRequest(
                RequestId: Interlocked.Increment(ref _nextRequestId),
                Kind: kind,
                RequestTimestamp: requestTimestamp,
                Trigger: trigger,
                Settings: settings.CreateSnapshot());

            snapshot = SetSnapshotLocked(
                state: _active ? _snapshot.State : CapturePipelineState.Queued);

            _signals.Writer.TryWrite(0);
        }

        PublishState(snapshot);

        return result;
    }

    public bool Stop(TimeSpan timeout)
    {
        CapturePipelineSnapshot? stoppingSnapshot = null;
        Thread? worker;

        lock (_gate)
        {
            worker = _workerThread;
            if (worker is null)
            {
                return true;
            }

            if (_acceptingRequests)
            {
                _acceptingRequests = false;
                _pendingRequest = null;
                stoppingSnapshot = SetSnapshotLocked(CapturePipelineState.Stopping);
                _signals.Writer.TryComplete();
            }
        }

        _snippingService.CancelActiveSelection(CaptureCancellationReason.Shutdown);

        if (stoppingSnapshot is not null)
        {
            PublishState(stoppingSnapshot);
        }

        if (ReferenceEquals(Thread.CurrentThread, worker))
        {
            return false;
        }

        return worker.Join(timeout);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (Stop(TimeSpan.FromSeconds(15)))
        {
            _workerStarted.Dispose();
        }
    }

    private void RunWorker()
    {
        try
        {
            _workerStarted.Set();

            while (_signals.Reader.WaitToReadAsync().AsTask().GetAwaiter().GetResult())
            {
                while (_signals.Reader.TryRead(out _))
                {
                    CaptureRequest? request;
                    CapturePipelineSnapshot startedSnapshot;

                    lock (_gate)
                    {
                        request = _pendingRequest;
                        if (request is null)
                        {
                            continue;
                        }

                        _pendingRequest = null;
                        _active = true;
                        _activeKind = request.Kind;
                        _activeTrigger = request.Trigger;
                        startedSnapshot = SetSnapshotLocked(CapturePipelineState.Capturing);
                    }

                    PublishState(startedSnapshot);

                    try
                    {
                        var execution = ExecuteRequest(request);
                        var result = execution.Result;

                        if (result is null)
                        {
                            PublishCancelled(new CaptureCancelledEvent(
                                request.RequestId,
                                request.Kind,
                                request.Trigger,
                                execution.CancellationReason ?? CaptureCancellationReason.User));

                            FinishRequest(CapturePipelineState.Cancelled);
                            continue;
                        }

                        PublishCompleted(new CaptureCompletedEvent(
                            request.RequestId,
                            request.Kind,
                            request.Trigger,
                            request.Settings,
                            result));

                        FinishRequest(CapturePipelineState.Completed);
                    }
                    catch (Exception exception)
                    {
                        PublishFailed(new CaptureFailedEvent(
                            request.RequestId,
                            request.Kind,
                            request.Trigger,
                            exception));

                        FinishRequest(CapturePipelineState.Failed);
                    }
                }
            }
        }
        catch (Exception exception)
        {
            _startupException = exception;
            _workerStarted.Set();

            CapturePipelineSnapshot failedSnapshot;
            lock (_gate)
            {
                _acceptingRequests = false;
                _active = false;
                _activeKind = null;
                _activeTrigger = null;
                failedSnapshot = SetSnapshotLocked(CapturePipelineState.Failed);
            }

            PublishState(failedSnapshot);
            PublishFailed(new CaptureFailedEvent(
                RequestId: 0,
                Kind: CaptureKind.FullDesktop,
                Trigger: "CaptureWorker",
                Exception: exception));
        }
        finally
        {
            CapturePipelineSnapshot stoppedSnapshot;
            lock (_gate)
            {
                _acceptingRequests = false;
                _active = false;
                _activeKind = null;
                _activeTrigger = null;
                _pendingRequest = null;
                stoppedSnapshot = SetSnapshotLocked(CapturePipelineState.Stopped);
            }

            PublishState(stoppedSnapshot);
        }
    }

    private CaptureExecutionResult ExecuteRequest(CaptureRequest request)
    {
        if (request.Kind == CaptureKind.FullDesktop)
        {
            return new CaptureExecutionResult(
                _captureService.CaptureFullDesktop(
                    request.Settings,
                    request.RequestTimestamp,
                    request.Trigger,
                    UpdateStage),
                CancellationReason: null);
        }

        if (request.Kind == CaptureKind.Region)
        {
            var outcome = _snippingService.CaptureRegion(
                request.Settings,
                request.RequestTimestamp,
                request.Trigger,
                UpdateStage);

            return new CaptureExecutionResult(
                outcome.Result,
                outcome.CancellationReason);
        }

        throw new ArgumentOutOfRangeException(
            nameof(request.Kind),
            request.Kind,
            "Unsupported capture kind.");
    }

    private void FinishRequest(CapturePipelineState finalState)
    {
        CapturePipelineSnapshot finishedSnapshot;

        lock (_gate)
        {
            _active = false;
            _activeKind = null;
            _activeTrigger = null;
            finishedSnapshot = SetSnapshotLocked(
                _pendingRequest is null
                    ? finalState
                    : CapturePipelineState.Queued);
        }

        PublishState(finishedSnapshot);
    }

    private void UpdateStage(CapturePipelineStage stage)
    {
        var state = stage switch
        {
            CapturePipelineStage.DirectoryPreparation => CapturePipelineState.Capturing,
            CapturePipelineStage.BitmapAllocation => CapturePipelineState.Capturing,
            CapturePipelineStage.PixelAcquisition => CapturePipelineState.Capturing,
            CapturePipelineStage.OverlayPreparation => CapturePipelineState.PreparingOverlay,
            CapturePipelineStage.RegionSelection => CapturePipelineState.Selecting,
            CapturePipelineStage.RegionCropping => CapturePipelineState.Cropping,
            CapturePipelineStage.PngPersistence => CapturePipelineState.Saving,
            CapturePipelineStage.ClipboardPublication => CapturePipelineState.Publishing,
            CapturePipelineStage.SoundDispatch => CapturePipelineState.Finalizing,
            CapturePipelineStage.Completed => CapturePipelineState.Completed,
            CapturePipelineStage.Cancelled => CapturePipelineState.Cancelled,
            _ => CapturePipelineState.Capturing,
        };

        CapturePipelineSnapshot snapshot;
        lock (_gate)
        {
            snapshot = SetSnapshotLocked(state);
        }

        PublishState(snapshot);
    }

    private CapturePipelineSnapshot SetSnapshotLocked(CapturePipelineState state)
    {
        _snapshot = new CapturePipelineSnapshot(
            Version: ++_snapshotVersion,
            State: state,
            HasActiveRequest: _active,
            HasPendingRequest: _pendingRequest is not null,
            ActiveKind: _activeKind,
            PendingKind: _pendingRequest?.Kind,
            ActiveTrigger: _activeTrigger,
            PendingTrigger: _pendingRequest?.Trigger);

        return _snapshot;
    }

    private void PublishState(CapturePipelineSnapshot snapshot)
    {
        InvokeSafely(StateChanged, snapshot);
    }

    private void PublishCompleted(CaptureCompletedEvent completed)
    {
        InvokeSafely(CaptureCompleted, completed);
    }

    private void PublishCancelled(CaptureCancelledEvent cancelled)
    {
        InvokeSafely(CaptureCancelled, cancelled);
    }

    private void PublishFailed(CaptureFailedEvent failed)
    {
        InvokeSafely(CaptureFailed, failed);
    }

    private static void InvokeSafely<T>(Action<T>? handlers, T value)
    {
        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList().Cast<Action<T>>())
        {
            try
            {
                handler(value);
            }
            catch
            {
                // Observer failures must not terminate the capture worker.
            }
        }
    }

    private sealed record CaptureRequest(
        long RequestId,
        CaptureKind Kind,
        long RequestTimestamp,
        string Trigger,
        AppSettings Settings);

    private sealed record CaptureExecutionResult(
        CaptureResult? Result,
        CaptureCancellationReason? CancellationReason);
}
