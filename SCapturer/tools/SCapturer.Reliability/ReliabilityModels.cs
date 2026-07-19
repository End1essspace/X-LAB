using System.Text.Json.Serialization;

namespace SCapturer.Reliability;

internal sealed record ReliabilityOptions(
    string AppPath,
    string OutputDirectory,
    int WarmupCaptures,
    int MeasuredCaptures,
    int ConsoleCycles,
    int RegionCancellationCycles,
    int ProcessLifecycleCycles,
    int CaptureTimeoutSeconds)
{
    public static ReliabilityOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (!argument.StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Unexpected argument: {argument}");
            }

            if (index + 1 >= args.Length)
            {
                throw new ArgumentException($"Missing value for {argument}.");
            }

            values[argument] = args[++index];
        }

        var appPath = Path.GetFullPath(Get(
            values,
            "--app",
            FindDefaultAppPath()));
        var output = Path.GetFullPath(Get(
            values,
            "--output",
            Path.Combine(
                Environment.CurrentDirectory,
                "artifacts",
                "reliability",
                DateTime.Now.ToString("yyyyMMdd-HHmmss"))));

        return new ReliabilityOptions(
            appPath,
            output,
            ReadInt(values, "--warmup-captures", 5, minimum: 1),
            ReadInt(values, "--captures", 100, minimum: 1),
            ReadInt(values, "--console-cycles", 30, minimum: 0),
            ReadInt(values, "--region-cancel-cycles", 5, minimum: 0),
            ReadInt(values, "--process-cycles", 10, minimum: 0),
            ReadInt(values, "--capture-timeout-seconds", 20, minimum: 5));
    }

    private static string FindDefaultAppPath()
    {
        var candidate = Path.Combine(
            Environment.CurrentDirectory,
            "src",
            "SCapturer.App",
            "bin",
            "Release",
            "net8.0-windows10.0.19041.0",
            "SCapturer.exe");

        return candidate;
    }

    private static string Get(
        IReadOnlyDictionary<string, string> values,
        string key,
        string fallback)
    {
        return values.TryGetValue(key, out var value)
            ? value
            : fallback;
    }

    private static int ReadInt(
        IReadOnlyDictionary<string, string> values,
        string key,
        int fallback,
        int minimum)
    {
        if (!values.TryGetValue(key, out var text))
        {
            return fallback;
        }

        if (!int.TryParse(text, out var parsed) || parsed < minimum)
        {
            throw new ArgumentException(
                $"{key} must be an integer greater than or equal to {minimum}.");
        }

        return parsed;
    }
}

internal sealed record ResourceSample(
    DateTimeOffset RecordedAtUtc,
    string Phase,
    int Iteration,
    int ProcessId,
    long WorkingSetBytes,
    long PrivateMemoryBytes,
    int HandleCount,
    int ThreadCount,
    uint GdiObjects,
    uint UserObjects);

internal sealed record ReliabilityGate(
    string Name,
    bool Passed,
    string Actual,
    string Limit);

internal sealed record LifecycleCycleResult(
    int Iteration,
    bool Started,
    bool ShowAccepted,
    bool HideAccepted,
    bool ExitAccepted,
    bool Exited,
    double TotalMilliseconds,
    string? ErrorMessage);

internal sealed record ReliabilitySummary(
    string SchemaVersion,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset FinishedAtUtc,
    string AppPath,
    string OutputDirectory,
    int WarmupCapturesRequested,
    int WarmupCapturesCompleted,
    int MeasuredCapturesRequested,
    int MeasuredCapturesCompleted,
    int ConsoleCyclesRequested,
    int ConsoleCyclesCompleted,
    int RegionCancellationCyclesRequested,
    int RegionCancellationCyclesCompleted,
    int ProcessLifecycleCyclesRequested,
    int ProcessLifecycleCyclesCompleted,
    int CommandFailures,
    int CaptureTimeouts,
    int UnexpectedRegionFiles,
    int TemporaryFilesRemaining,
    ResourceSample? BaselineResources,
    ResourceSample? FinalResources,
    IReadOnlyList<ReliabilityGate> Gates,
    IReadOnlyList<LifecycleCycleResult> LifecycleCycles,
    bool Passed,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings)
{
    [JsonIgnore]
    public int ExitCode => Passed ? 0 : 1;
}
