namespace SCapturer.Core.Benchmarking;

internal static class BenchmarkStatistics
{
    public static BenchmarkSummary CreateSummary(
        IReadOnlyList<BenchmarkSample> samples)
    {
        if (samples.Count == 0)
        {
            throw new ArgumentException(
                "At least one benchmark sample is required.",
                nameof(samples));
        }

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

    private static double Percentile(
        IEnumerable<double> source,
        double percentile)
    {
        var ordered = source.OrderBy(value => value).ToArray();
        var index = Math.Clamp(
            (int)Math.Ceiling(percentile * ordered.Length) - 1,
            0,
            ordered.Length - 1);

        return ordered[index];
    }
}
