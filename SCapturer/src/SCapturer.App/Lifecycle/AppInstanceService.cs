using System.IO.Pipes;
using System.Text;

namespace SCapturer.App.Lifecycle;

internal sealed class AppInstanceService : IDisposable
{
    public const string InstanceSuffixEnvironmentVariable =
        "SCAPTURER_INSTANCE_SUFFIX";

    private static readonly string? InstanceSuffix = CreateInstanceSuffix();
    private static readonly string PipeName = InstanceSuffix is null
        ? "SCapturer.App.Session." +
            System.Diagnostics.Process.GetCurrentProcess().SessionId
        : $"SCapturer.App.{InstanceSuffix}.Session." +
            System.Diagnostics.Process.GetCurrentProcess().SessionId;

    private readonly CancellationTokenSource _shutdown = new();
    private Task? _serverTask;
    private int _disposed;

    public static string MutexName => InstanceSuffix is null
        ? @"Local\SCapturer.App"
        : $@"Local\SCapturer.App.{InstanceSuffix}";

    public event Action<AppInstanceCommand>? CommandReceived;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) == 1,
            this);

        _serverTask ??= Task.Run(() => RunServerAsync(_shutdown.Token));
    }

    public static bool TrySend(
        AppInstanceCommand command,
        TimeSpan timeout,
        out string? errorMessage)
    {
        errorMessage = null;

        if (command == AppInstanceCommand.None)
        {
            return true;
        }

        var deadline = DateTime.UtcNow + timeout;
        Exception? lastException = null;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var client = new NamedPipeClientStream(
                    serverName: ".",
                    pipeName: PipeName,
                    direction: PipeDirection.Out,
                    options: PipeOptions.Asynchronous |
                        PipeOptions.CurrentUserOnly);

                var remaining = deadline - DateTime.UtcNow;
                var connectTimeout = Math.Clamp(
                    (int)remaining.TotalMilliseconds,
                    50,
                    500);

                client.Connect(connectTimeout);

                using var writer = new StreamWriter(
                    client,
                    new UTF8Encoding(
                        encoderShouldEmitUTF8Identifier: false),
                    bufferSize: 256,
                    leaveOpen: true)
                {
                    AutoFlush = true,
                };

                writer.WriteLine(command.ToString());
                return true;
            }
            catch (Exception exception)
                when (exception is TimeoutException or IOException)
            {
                lastException = exception;
                Thread.Sleep(75);
            }
        }

        errorMessage =
            "The running SCapturer instance did not accept the activation command" +
            (lastException is null
                ? "."
                : $": {lastException.GetBaseException().Message}");
        return false;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        _shutdown.Cancel();

        try
        {
            _serverTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException exception)
            when (exception.InnerExceptions.All(
                inner => inner is OperationCanceledException))
        {
            // Cancellation is expected during shutdown.
        }

        _shutdown.Dispose();
    }

    private async Task RunServerAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.In,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

                await server.WaitForConnectionAsync(cancellationToken)
                    .ConfigureAwait(false);

                using var reader = new StreamReader(
                    server,
                    Encoding.UTF8,
                    detectEncodingFromByteOrderMarks: true,
                    bufferSize: 256,
                    leaveOpen: true);

                var line = await reader.ReadLineAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (Enum.TryParse<AppInstanceCommand>(
                        line,
                        ignoreCase: true,
                        out var command) &&
                    command != AppInstanceCommand.None)
                {
                    Publish(command);
                }
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (IOException)
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(100, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception)
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(250, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }
    }

    private void Publish(AppInstanceCommand command)
    {
        var handlers = CommandReceived;
        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList()
                     .Cast<Action<AppInstanceCommand>>())
        {
            try
            {
                handler(command);
            }
            catch
            {
                // An activation observer cannot terminate the IPC server.
            }
        }
    }

    private static string? CreateInstanceSuffix()
    {
        var suffix = Environment.GetEnvironmentVariable(
            InstanceSuffixEnvironmentVariable);

        if (string.IsNullOrWhiteSpace(suffix))
        {
            return null;
        }

        var normalized = new string(suffix
            .Where(character => char.IsLetterOrDigit(character) ||
                character is '-' or '_')
            .Take(48)
            .ToArray());

        return string.IsNullOrWhiteSpace(normalized)
            ? null
            : normalized;
    }
}
