
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

        var benchmarkSettings = new AppSettings
        {
            FullCaptureFolder = benchmarkCaptureFolder,
            CopyToClipboard = false,
            PlayCaptureSound = false,
            EnableDiagnostics = false,
        };

        Directory.CreateDirectory(benchmarkCaptureFolder);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Invoke(new BenchmarkProgress("Warm-up", 1, 1));
            var warmup = _captureService.CaptureFullDesktop(
                benchmarkSettings,
                trigger: "BenchmarkWarmup");
            TryDelete(warmup.FilePath);

            var samples = new List<BenchmarkSample>(measuredIterations);

            for (var iteration = 1; iteration <= measuredIterations; iteration++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Invoke(new BenchmarkProgress(
                    "Measured capture",
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
                    Metrics: result.Metrics));

                TryDelete(result.FilePath);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var summary = CreateSummary(samples);
            var first = samples[0];

            var report = new BenchmarkReport(
                SchemaVersion: "1.0",
                CreatedAtUtc: DateTimeOffset.UtcNow,
                OperatingSystem: RuntimeInformation.OSDescription,
                RuntimeVersion: RuntimeInformation.FrameworkDescription,
                ProcessArchitecture: RuntimeInformation.ProcessArchitecture.ToString(),
                ProcessorCount: Environment.ProcessorCount,
                BenchmarkCaptureFolder: benchmarkCaptureFolder,
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

            return new BenchmarkRunResult(reportFilePath, summary);
        }
        finally
        {
            TryDeleteEmptyDirectory(benchmarkCaptureFolder);
        }
    }

    private static BenchmarkSummary CreateSummary(IReadOnlyList<BenchmarkSample> samples)
    {
        var total = samples.Select(sample => sample.Metrics.TotalMilliseconds).ToArray();
        var pixel = samples.Select(sample => sample.Metrics.PixelAcquisitionMilliseconds).ToArray();
        var png = samples.Select(sample => sample.Metrics.PngPersistenceMilliseconds).ToArray();

        return new BenchmarkSummary(
            MedianTotalMilliseconds: Median(total),
            P95TotalMilliseconds: Percentile(total, 0.95),
            FastestTotalMilliseconds: total.Min(),
            SlowestTotalMilliseconds: total.Max(),
            MedianPixelAcquisitionMilliseconds: Median(pixel),
            MedianPngPersistenceMilliseconds: Median(png),
            AverageManagedAllocatedBytes: (long)samples.Average(
                sample => (double)sample.Metrics.ManagedAllocatedBytes),
            AverageFileSizeBytes: (long)samples.Average(
                sample => (double)sample.FileSizeBytes));
    }

    private static double Median(IEnumerable<double> source)
    {
        var ordered = source.OrderBy(value => value).ToArray();
        var midpoint = ordered.Length / 2;

        return ordered.Length % 2 == 0
            ? (ordered[midpoint - 1] + ordered[midpoint]) / 2
            : ordered[midpoint];
    }

    private static double Percentile(IEnumerable<double> source, double percentile)
    {
        var ordered = source.OrderBy(value => value).ToArray();
        var index = Math.Clamp(
            (int)Math.Ceiling(percentile * ordered.Length) - 1,
            0,
            ordered.Length - 1);

        return ordered[index];
    }

    private static void TryDelete(string filePath)
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

    private static void TryDeleteEmptyDirectory(string directoryPath)
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
}
