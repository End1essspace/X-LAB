using SCapturer.Core.Capture;
using SCapturer.Core.Diagnostics;

namespace SCapturer.Core.Benchmarking;

public sealed record BenchmarkProgress(
    string Phase,
    int CurrentIteration,
    int TotalIterations);

public sealed record BenchmarkSample(
    int Iteration,
    int Width,
    int Height,
    long FileSizeBytes,
    string BackendName,
    CaptureMetrics Metrics);

public sealed record BenchmarkSummary(
    double MedianTotalMilliseconds,
    double P95TotalMilliseconds,
    double FastestTotalMilliseconds,
    double SlowestTotalMilliseconds,
    double MedianPixelAcquisitionMilliseconds,
    double MedianPngPersistenceMilliseconds,
    long AverageManagedAllocatedBytes,
    long AverageFileSizeBytes);

public sealed record BenchmarkReport(
    string SchemaVersion,
    DateTimeOffset CreatedAtUtc,
    string OperatingSystem,
    string RuntimeVersion,
    string ProcessArchitecture,
    int ProcessorCount,
    string BenchmarkCaptureFolder,
    string BackendName,
    CaptureBackendMode BackendMode,
    int WarmupIterations,
    int MeasuredIterations,
    int Width,
    int Height,
    BenchmarkSummary Summary,
    IReadOnlyList<BenchmarkSample> Samples);

public sealed record BenchmarkRunResult(
    string ReportFilePath,
    string BackendName,
    BenchmarkSummary Summary);

public sealed record BackendComparisonSide(
    CaptureBackendMode Mode,
    string BackendName,
    BenchmarkSummary Summary,
    IReadOnlyList<BenchmarkSample> Samples);

public sealed record BackendComparisonDecision(
    CaptureBackendMode RecommendedMode,
    string RecommendedBackend,
    double NativeP95ImprovementPercent,
    double NativeAllocationImprovementPercent,
    string Reason);

public sealed record BackendComparisonReport(
    string SchemaVersion,
    DateTimeOffset CreatedAtUtc,
    string OperatingSystem,
    string RuntimeVersion,
    string ProcessArchitecture,
    int ProcessorCount,
    string BenchmarkCaptureFolder,
    int WarmupIterationsPerBackend,
    int MeasuredIterationsPerBackend,
    BackendComparisonSide Reference,
    BackendComparisonSide Native,
    BackendComparisonDecision Decision);

public sealed record BackendComparisonRunResult(
    string ReportFilePath,
    BackendComparisonDecision Decision,
    BackendComparisonSide Reference,
    BackendComparisonSide Native);
