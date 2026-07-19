using System.Collections.Concurrent;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SCapturer.Core.Persistence;

public sealed record ClipboardPublicationResult(
    bool Success,
    int Attempts,
    string? ErrorMessage)
{
    public static ClipboardPublicationResult Succeeded(int attempts)
    {
        return new ClipboardPublicationResult(true, attempts, ErrorMessage: null);
    }

    public static ClipboardPublicationResult Failed(
        int attempts,
        string errorMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        return new ClipboardPublicationResult(false, attempts, errorMessage);
    }
}

public sealed class ClipboardPublicationService : IDisposable
{
    private static readonly TimeSpan PublicationTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan CallerWaitTimeout = TimeSpan.FromSeconds(3);

    private readonly BlockingCollection<ClipboardRequest> _requests = new(1);
    private readonly ManualResetEventSlim _workerStarted = new(false);
    private readonly Thread _workerThread;

    private Exception? _startupException;
    private int _disposed;

    public ClipboardPublicationService()
    {
        _workerThread = new Thread(RunWorker)
        {
            IsBackground = true,
            Name = "SCapturer Clipboard Dispatcher",
        };
        _workerThread.SetApartmentState(ApartmentState.STA);
        _workerThread.Start();

        if (!_workerStarted.Wait(TimeSpan.FromSeconds(5)))
        {
            throw new TimeoutException(
                "Timed out while starting the clipboard dispatcher.");
        }

        if (_startupException is not null)
        {
            throw new InvalidOperationException(
                "The clipboard dispatcher could not start.",
                _startupException);
        }
    }

    public ClipboardPublicationResult Publish(Image source)
    {
        ArgumentNullException.ThrowIfNull(source);
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) == 1,
            this);

        Bitmap clone;
        try
        {
            clone = new Bitmap(source);
        }
        catch (Exception exception)
        {
            return ClipboardPublicationResult.Failed(
                attempts: 0,
                $"Could not create an isolated clipboard image: " +
                exception.GetBaseException().Message);
        }

        var request = new ClipboardRequest(clone);

        if (!_requests.TryAdd(request, millisecondsTimeout: 250))
        {
            clone.Dispose();
            return ClipboardPublicationResult.Failed(
                attempts: 0,
                "The clipboard dispatcher queue is busy.");
        }

        if (!request.Completion.Task.Wait(CallerWaitTimeout))
        {
            return ClipboardPublicationResult.Failed(
                attempts: 0,
                "Clipboard publication timed out. The PNG remains safely saved.");
        }

        return request.Completion.Task.GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        _requests.CompleteAdding();
        var stopped = _workerThread.Join(TimeSpan.FromSeconds(5));

        if (stopped)
        {
            while (_requests.TryTake(out var pending))
            {
                pending.Image.Dispose();
                pending.Completion.TrySetResult(
                    ClipboardPublicationResult.Failed(
                        attempts: 0,
                        "Clipboard publication was cancelled during shutdown."));
            }

            _requests.Dispose();
            _workerStarted.Dispose();
        }
    }

    private void RunWorker()
    {
        try
        {
            _workerStarted.Set();

            foreach (var request in _requests.GetConsumingEnumerable())
            {
                using (request.Image)
                {
                    request.Completion.TrySetResult(
                        PublishWithRetry(request.Image));
                }
            }
        }
        catch (Exception exception)
        {
            _startupException = exception;
            _workerStarted.Set();

            while (_requests.TryTake(out var pending))
            {
                pending.Image.Dispose();
                pending.Completion.TrySetResult(
                    ClipboardPublicationResult.Failed(
                        attempts: 0,
                        "The clipboard dispatcher stopped unexpectedly: " +
                        exception.GetBaseException().Message));
            }
        }
    }

    private static ClipboardPublicationResult PublishWithRetry(Image image)
    {
        var deadline = DateTime.UtcNow + PublicationTimeout;
        var delayMilliseconds = 25;
        var attempts = 0;
        string? lastError = null;

        while (DateTime.UtcNow < deadline)
        {
            attempts++;

            try
            {
                Clipboard.SetImage(image);
                return ClipboardPublicationResult.Succeeded(attempts);
            }
            catch (ExternalException exception)
            {
                lastError = exception.Message;
            }
            catch (ThreadStateException exception)
            {
                return ClipboardPublicationResult.Failed(
                    attempts,
                    "The clipboard dispatcher is not running in STA mode: " +
                    exception.Message);
            }
            catch (Exception exception)
            {
                return ClipboardPublicationResult.Failed(
                    attempts,
                    "Clipboard publication failed: " +
                    exception.GetBaseException().Message);
            }

            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                break;
            }

            var sleep = Math.Min(
                delayMilliseconds,
                Math.Max(1, (int)remaining.TotalMilliseconds));
            Thread.Sleep(sleep);
            delayMilliseconds = Math.Min(delayMilliseconds * 2, 400);
        }

        return ClipboardPublicationResult.Failed(
            attempts,
            "Windows kept the clipboard locked for the complete retry window" +
            (string.IsNullOrWhiteSpace(lastError) ? "." : $": {lastError}"));
    }

    private sealed record ClipboardRequest(
        Bitmap Image,
        TaskCompletionSource<ClipboardPublicationResult> Completion)
    {
        public ClipboardRequest(Bitmap image)
            : this(
                image,
                new TaskCompletionSource<ClipboardPublicationResult>(
                    TaskCreationOptions.RunContinuationsAsynchronously))
        {
        }
    }
}
