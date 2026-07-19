namespace SCapturer.Reliability;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            var options = ReliabilityOptions.Parse(args);
            Console.WriteLine("SCapturer reliability harness");
            Console.WriteLine($"Application : {options.AppPath}");
            Console.WriteLine($"Output      : {options.OutputDirectory}");
            Console.WriteLine(
                $"Workload    : {options.WarmupCaptures} warm-up, " +
                $"{options.MeasuredCaptures} captures, " +
                $"{options.ConsoleCycles} console cycles, " +
                $"{options.RegionCancellationCycles} region cancellations, " +
                $"{options.ProcessLifecycleCycles} process cycles");
            Console.WriteLine();

            var summary = new ReliabilityRunner(options).Run();

            Console.WriteLine();
            Console.WriteLine(summary.Passed
                ? "RELIABILITY GATE: PASS"
                : "RELIABILITY GATE: FAIL");
            Console.WriteLine(
                Path.Combine(options.OutputDirectory, "reliability-report.md"));
            return summary.ExitCode;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("Reliability harness could not start:");
            Console.Error.WriteLine(exception.GetBaseException().Message);
            return 2;
        }
    }
}
