using System.Runtime.InteropServices;
using System.Text.Json;
using SCapturer.Core.Models;
using SCapturer.Core.Services;

namespace SCapturer.Core.Benchmarking;

public sealed class BaselineBenchmarkService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly CaptureService _captureService;
    private readonly AppPaths _paths;

    public BaselineBenchmarkService(CaptureService captureService, AppPaths paths)
    {
        _captureService = captureService;
        _paths = paths;
    }

    public BenchmarkRunResult Run(
        AppSettings sourceSettings,
        int measuredIterations = 10,
        Action<BenchmarkProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceSettings);
        cancellationToken.ThrowIfCancellationRequested();

        if (measuredIterations < 3)
        {
            throw new ArgumentOutOfRangeException(
                nameof(measuredIterations),
                "At least three measured iterations are required.");
        }

        var benchmarkCaptureFolder = Path.Combine(
            sourceSettings.FullCaptureFolder,
            ".scapturer-benchmark");

        var benchmarkSettings = CreateBenchmarkSettings(
            sourceSettings,
            benchmarkCaptureFolder);

        Directory.CreateDirectory(benchmarkCaptureFolder);

        try
        {
            var samples = RunSamples(
                benchmarkSettings,
                measuredIterations,
                progress,
                cancellationToken);

            var summary = BenchmarkStatistics.CreateSummary(samples);
            var first = samples[0];

            var report = new BenchmarkReport(
                SchemaVersion: "2.0",
                CreatedAtUtc: DateTimeOffset.UtcNow,
                OperatingSystem: RuntimeInformation.OSDescription,
                RuntimeVersion: RuntimeInformation.FrameworkDescription,
                ProcessArchitecture: RuntimeInformation.ProcessArchitecture.ToString(),
                ProcessorCount: Environment.ProcessorCount,
                BenchmarkCaptureFolder: benchmarkCaptureFolder,
                BackendName: first.BackendName,
                BackendMode: benchmarkSettings.CaptureBackend,
                WarmupIterations: 1,
                MeasuredIterations: measuredIterations,
                Width: first.Width,
                Height: first.Height,
                Summary: summary,
                Samples: samples);

            Directory.CreateDirectory(_paths.BenchmarkReportsDirectory);
            var reportFilePath = Path.Combine(
                _paths.BenchmarkReportsDirectory,
                $"baseline_{DateTime.Now:yyyyMMdd-HHmmss-fff}.json");

            File.WriteAllText(
                reportFilePath,
                JsonSerializer.Serialize(report, JsonOptions));

            return new BenchmarkRunResult(
                reportFilePath,
                first.BackendName,
                summary);
        }
        finally
        {
            TryDeleteEmptyDirectory(benchmarkCaptureFolder);
        }
    }

    internal IReadOnlyList<BenchmarkSample> RunSamples(
        AppSettings benchmarkSettings,
        int measuredIterations,
        Action<BenchmarkProgress>? progress,
        CancellationToken cancellationToken,
        string phasePrefix = "")
    {
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Invoke(new BenchmarkProgress(
            Prefix(phasePrefix, "Warm-up"),
            1,
            1));

        var warmup = _captureService.CaptureFullDesktop(
            benchmarkSettings,
            trigger: "BenchmarkWarmup");
        TryDelete(warmup.FilePath);

        var samples = new List<BenchmarkSample>(measuredIterations);

        for (var iteration = 1; iteration <= measuredIterations; iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Invoke(new BenchmarkProgress(
                Prefix(phasePrefix, "Measured capture"),
                iteration,
                measuredIterations));

            var result = _captureService.CaptureFullDesktop(
                benchmarkSettings,
                trigger: "Benchmark");

            samples.Add(new BenchmarkSample(
                Iteration: iteration,
                Width: result.Width,
                Height: result.Height,
                FileSizeBytes: result.FileSizeBytes,
                BackendName: result.BackendName,
                Metrics: result.Metrics));

            TryDelete(result.FilePath);
        }

        return samples;
    }

    internal static AppSettings CreateBenchmarkSettings(
        AppSettings sourceSettings,
        string benchmarkCaptureFolder)
    {
        return new AppSettings
        {
            FullCaptureFolder = benchmarkCaptureFolder,
            SnipCaptureFolder = sourceSettings.SnipCaptureFolder,
            CopyToClipboard = false,
            PlayCaptureSound = false,
            EnableDiagnostics = false,
            CaptureBackend = sourceSettings.CaptureBackend,
            FullCaptureHotkey = sourceSettings.FullCaptureHotkey.CreateSnapshot(),
            RegionCaptureHotkey = sourceSettings.RegionCaptureHotkey.CreateSnapshot(),
            ExitHotkey = sourceSettings.ExitHotkey.CreateSnapshot(),
            ToggleConsoleHotkey = sourceSettings.ToggleConsoleHotkey.CreateSnapshot(),
        };
    }

    internal static void TryDelete(string filePath)
    {
        try
        {
            File.Delete(filePath);
        }
        catch (IOException)
        {
            // Benchmark reports remain useful even if cleanup is delayed.
        }
        catch (UnauthorizedAccessException)
        {
            // The temporary image may be cleaned up manually later.
        }
    }

    internal static void TryDeleteEmptyDirectory(string directoryPath)
    {
        try
        {
            if (Directory.Exists(directoryPath) &&
                !Directory.EnumerateFileSystemEntries(directoryPath).Any())
            {
                Directory.Delete(directoryPath);
            }
        }
        catch (IOException)
        {
            // Cleanup is best effort and must not hide benchmark results.
        }
        catch (UnauthorizedAccessException)
        {
            // Cleanup is best effort and must not hide benchmark results.
        }
    }

    private static string Prefix(string prefix, string value)
    {
        return string.IsNullOrWhiteSpace(prefix)
            ? value
            : $"{prefix}: {value}";
    }
}
