
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
    int WarmupIterations,
    int MeasuredIterations,
    int Width,
    int Height,
    BenchmarkSummary Summary,
    IReadOnlyList<BenchmarkSample> Samples);

public sealed record BenchmarkRunResult(
    string ReportFilePath,
    BenchmarkSummary Summary);
