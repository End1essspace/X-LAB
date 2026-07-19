using System.Linq;
using System.Threading.Channels;
using SCapturer.Core.Models;
using SCapturer.Core.Services;

namespace SCapturer.Core.Pipeline;

public sealed class CaptureCoordinator : IDisposable
{
    private readonly CaptureService _captureService;
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
    private string? _activeTrigger;

    public CaptureCoordinator(CaptureService captureService)
    {
        _captureService = captureService;

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
                RequestTimestamp: requestTimestamp,
                Trigger: trigger,
                Settings: settings.CreateSnapshot());

            snapshot = SetSnapshotLocked(
                state: _active ? _snapshot.State : CapturePipelineState.Queued);

            // The channel is a bounded wake-up signal. The actual pending slot
            // lives under _gate so repeated requests replace only the one
            // queued request. Signalling inside the lock prevents shutdown from
            // completing the writer between storing and waking the request.
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
                stoppingSnapshot = SetSnapshotLocked(CapturePipelineState.Stopping);
                _signals.Writer.TryComplete();
            }
        }

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
                        _activeTrigger = request.Trigger;
                        startedSnapshot = SetSnapshotLocked(CapturePipelineState.Capturing);
                    }

                    PublishState(startedSnapshot);

                    try
                    {
                        var result = _captureService.CaptureFullDesktop(
                            request.Settings,
                            request.RequestTimestamp,
                            request.Trigger,
                            stage => UpdateStage(stage));

                        PublishCompleted(new CaptureCompletedEvent(
                            request.RequestId,
                            request.Trigger,
                            request.Settings,
                            result));

                        CapturePipelineSnapshot finishedSnapshot;
                        lock (_gate)
                        {
                            _active = false;
                            _activeTrigger = null;
                            finishedSnapshot = SetSnapshotLocked(
                                _pendingRequest is null
                                    ? CapturePipelineState.Completed
                                    : CapturePipelineState.Queued);
                        }

                        PublishState(finishedSnapshot);
                    }
                    catch (Exception exception)
                    {
                        PublishFailed(new CaptureFailedEvent(
                            request.RequestId,
                            request.Trigger,
                            exception));

                        CapturePipelineSnapshot failedSnapshot;
                        lock (_gate)
                        {
                            _active = false;
                            _activeTrigger = null;
                            failedSnapshot = SetSnapshotLocked(
                                _pendingRequest is null
                                    ? CapturePipelineState.Failed
                                    : CapturePipelineState.Queued);
                        }

                        PublishState(failedSnapshot);
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
                _activeTrigger = null;
                failedSnapshot = SetSnapshotLocked(CapturePipelineState.Failed);
            }

            PublishState(failedSnapshot);
            PublishFailed(new CaptureFailedEvent(
                RequestId: 0,
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
                _activeTrigger = null;
                _pendingRequest = null;
                stoppedSnapshot = SetSnapshotLocked(CapturePipelineState.Stopped);
            }

            PublishState(stoppedSnapshot);
        }
    }

    private void UpdateStage(CapturePipelineStage stage)
    {
        var state = stage switch
        {
            CapturePipelineStage.DirectoryPreparation => CapturePipelineState.Capturing,
            CapturePipelineStage.BitmapAllocation => CapturePipelineState.Capturing,
            CapturePipelineStage.PixelAcquisition => CapturePipelineState.Capturing,
            CapturePipelineStage.PngPersistence => CapturePipelineState.Saving,
            CapturePipelineStage.ClipboardPublication => CapturePipelineState.Publishing,
            CapturePipelineStage.SoundDispatch => CapturePipelineState.Finalizing,
            CapturePipelineStage.Completed => CapturePipelineState.Completed,
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
        long RequestTimestamp,
        string Trigger,
        AppSettings Settings);
}
