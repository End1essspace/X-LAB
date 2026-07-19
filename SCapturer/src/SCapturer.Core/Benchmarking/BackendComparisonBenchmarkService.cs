using System.Runtime.InteropServices;
using System.Text.Json;
using SCapturer.Core.Capture;
using SCapturer.Core.Models;
using SCapturer.Core.Services;

namespace SCapturer.Core.Benchmarking;

public sealed class BackendComparisonBenchmarkService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly BaselineBenchmarkService _baseline;
    private readonly CaptureBackendProvider _backendProvider;
    private readonly AppPaths _paths;

    public BackendComparisonBenchmarkService(
        BaselineBenchmarkService baseline,
        CaptureBackendProvider backendProvider,
        AppPaths paths)
    {
        _baseline = baseline;
        _backendProvider = backendProvider;
        _paths = paths;
    }

    public BackendComparisonRunResult Run(
        AppSettings sourceSettings,
        int measuredIterations = 10,
        Action<BenchmarkProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceSettings);

        if (measuredIterations < 3)
        {
            throw new ArgumentOutOfRangeException(
                nameof(measuredIterations),
                "At least three measured iterations are required.");
        }

        if (!_backendProvider.IsNativeAvailable(out var unavailableReason))
        {
            throw new InvalidOperationException(
                unavailableReason ?? "The native backend is unavailable.");
        }

        var benchmarkFolder = Path.Combine(
            sourceSettings.FullCaptureFolder,
            ".scapturer-backend-comparison");

        Directory.CreateDirectory(benchmarkFolder);

        try
        {
            var referenceSettings = BaselineBenchmarkService.CreateBenchmarkSettings(
                sourceSettings,
                benchmarkFolder);
            referenceSettings.CaptureBackend = CaptureBackendMode.ReferenceGdiPlus;

            var nativeSettings = BaselineBenchmarkService.CreateBenchmarkSettings(
                sourceSettings,
                benchmarkFolder);
            nativeSettings.CaptureBackend = CaptureBackendMode.NativeGdiWic;

            var referenceSamples = _baseline.RunSamples(
                referenceSettings,
                measuredIterations,
                progress,
                cancellationToken,
                phasePrefix: "Reference GDI+");

            var nativeSamples = _baseline.RunSamples(
                nativeSettings,
                measuredIterations,
                progress,
                cancellationToken,
                phasePrefix: "Native GDI + WIC");

            var reference = new BackendComparisonSide(
                CaptureBackendMode.ReferenceGdiPlus,
                referenceSamples[0].BackendName,
                BenchmarkStatistics.CreateSummary(referenceSamples),
                referenceSamples);

            var native = new BackendComparisonSide(
                CaptureBackendMode.NativeGdiWic,
                nativeSamples[0].BackendName,
                BenchmarkStatistics.CreateSummary(nativeSamples),
                nativeSamples);

            var decision = CreateDecision(reference, native);

            var report = new BackendComparisonReport(
                SchemaVersion: "1.0",
                CreatedAtUtc: DateTimeOffset.UtcNow,
                OperatingSystem: RuntimeInformation.OSDescription,
                RuntimeVersion: RuntimeInformation.FrameworkDescription,
                ProcessArchitecture: RuntimeInformation.ProcessArchitecture.ToString(),
                ProcessorCount: Environment.ProcessorCount,
                BenchmarkCaptureFolder: benchmarkFolder,
                WarmupIterationsPerBackend: 1,
                MeasuredIterationsPerBackend: measuredIterations,
                Reference: reference,
                Native: native,
                Decision: decision);

            Directory.CreateDirectory(_paths.BenchmarkReportsDirectory);
            var reportPath = Path.Combine(
                _paths.BenchmarkReportsDirectory,
                $"backend-comparison_{DateTime.Now:yyyyMMdd-HHmmss-fff}.json");

            File.WriteAllText(
                reportPath,
                JsonSerializer.Serialize(report, JsonOptions));

            return new BackendComparisonRunResult(
                reportPath,
                decision,
                reference,
                native);
        }
        finally
        {
            BaselineBenchmarkService.TryDeleteEmptyDirectory(benchmarkFolder);
        }
    }

    private static BackendComparisonDecision CreateDecision(
        BackendComparisonSide reference,
        BackendComparisonSide native)
    {
        var p95Improvement = ImprovementPercent(
            reference.Summary.P95TotalMilliseconds,
            native.Summary.P95TotalMilliseconds);

        var allocationImprovement = ImprovementPercent(
            reference.Summary.AverageManagedAllocatedBytes,
            native.Summary.AverageManagedAllocatedBytes);

        var nativeTotalRegression = native.Summary.MedianTotalMilliseconds >
            reference.Summary.MedianTotalMilliseconds * 1.05;

        var nativeWins = !nativeTotalRegression &&
            (p95Improvement >= 20 || allocationImprovement >= 20);

        if (nativeWins)
        {
            return new BackendComparisonDecision(
                CaptureBackendMode.NativeGdiWic,
                native.BackendName,
                p95Improvement,
                allocationImprovement,
                "Native met the P7 gate: at least 20% p95 or managed-allocation improvement without a median-total regression above 5%.");
        }

        return new BackendComparisonDecision(
            CaptureBackendMode.ReferenceGdiPlus,
            reference.BackendName,
            p95Improvement,
            allocationImprovement,
            nativeTotalRegression
                ? "Reference retained because native median total latency regressed by more than 5%."
                : "Reference retained because native did not reach the 20% p95 or managed-allocation improvement gate.");
    }

    private static double ImprovementPercent(double baseline, double candidate)
    {
        if (baseline <= 0)
        {
            return 0;
        }

        return (baseline - candidate) / baseline * 100;
    }
}
