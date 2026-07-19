using System.Diagnostics;
using System.Text;
using System.Text.Json;
using SCapturer.Core.Capture;
using SCapturer.Core.Models;
using SCapturer.Core.Services;

namespace SCapturer.Reliability;

internal sealed class ReliabilityRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly JsonSerializerOptions JsonLineOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly ReliabilityOptions _options;
    private readonly List<ResourceSample> _samples = [];
    private readonly List<LifecycleCycleResult> _lifecycleResults = [];
    private readonly List<string> _errors = [];
    private readonly List<string> _warnings = [];
    private readonly Dictionary<string, string> _environment;
    private readonly string _instanceSuffix;
    private readonly string _dataDirectory;
    private readonly string _fullCaptureDirectory;
    private readonly string _snipCaptureDirectory;
    private readonly string _resourceSamplesPath;

    private int _commandFailures;
    private int _captureTimeouts;
    private int _unexpectedRegionFiles;
    private int _warmupCompleted;
    private int _measuredCompleted;
    private int _consoleCyclesCompleted;
    private int _regionCancellationCyclesCompleted;

    public ReliabilityRunner(ReliabilityOptions options)
    {
        _options = options;
        _dataDirectory = Path.Combine(options.OutputDirectory, "app-data");
        _fullCaptureDirectory = Path.Combine(
            options.OutputDirectory,
            "captures",
            "Full");
        _snipCaptureDirectory = Path.Combine(
            options.OutputDirectory,
            "captures",
            "Snips");
        _resourceSamplesPath = Path.Combine(
            options.OutputDirectory,
            "resource-samples.jsonl");

        _instanceSuffix = "Reliability_" + Guid.NewGuid().ToString("N");
        _environment = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["SCAPTURER_INSTANCE_SUFFIX"] = _instanceSuffix,
            [AppPaths.DataDirectoryEnvironmentVariable] = _dataDirectory,
            ["SCAPTURER_DISABLE_HOTKEYS"] = "1",
            ["SCAPTURER_NONINTERACTIVE"] = "1",
        };
    }

    public ReliabilitySummary Run()
    {
        var startedAt = DateTimeOffset.UtcNow;
        Directory.CreateDirectory(_options.OutputDirectory);
        Directory.CreateDirectory(_fullCaptureDirectory);
        Directory.CreateDirectory(_snipCaptureDirectory);
        PrepareIsolatedSettings();

        ResourceSample? baseline = null;
        ResourceSample? final = null;
        Process? primary = null;

        try
        {
            ValidateEnvironment();
            primary = StartPrimary();
            WaitForPrimaryReady(primary);

            for (var iteration = 1;
                 iteration <= _options.WarmupCaptures;
                 iteration++)
            {
                if (RunCapture(primary, "warmup", iteration))
                {
                    _warmupCompleted++;
                }
            }

            WarmRepresentativeResourcePaths(primary);
            Thread.Sleep(2000);
            baseline = Sample(primary, "baseline", 0);

            for (var iteration = 1;
                 iteration <= _options.MeasuredCaptures;
                 iteration++)
            {
                if (RunCapture(primary, "capture", iteration))
                {
                    _measuredCompleted++;
                }

                if (iteration == 1 ||
                    iteration % 10 == 0 ||
                    iteration == _options.MeasuredCaptures)
                {
                    Sample(primary, "capture", iteration);
                }
            }

            for (var iteration = 1;
                 iteration <= _options.ConsoleCycles;
                 iteration++)
            {
                var show = SendCommand("--show");
                Thread.Sleep(50);
                var hide = SendCommand("--hide");

                if (show && hide)
                {
                    _consoleCyclesCompleted++;
                }

                if (iteration == 1 ||
                    iteration % 10 == 0 ||
                    iteration == _options.ConsoleCycles)
                {
                    Sample(primary, "console-cycle", iteration);
                }
            }

            for (var iteration = 1;
                 iteration <= _options.RegionCancellationCycles;
                 iteration++)
            {
                if (RunRegionCancellation(primary, iteration))
                {
                    _regionCancellationCyclesCompleted++;
                }

                Sample(primary, "region-cancel", iteration);
            }

            Thread.Sleep(2000);
            final = Sample(primary, "final", 0);
        }
        catch (Exception exception)
        {
            _errors.Add(exception.GetBaseException().Message);
        }
        finally
        {
            StopPrimary(primary);
        }

        RunRepeatedProcessLifecycle();

        var temporaryFilesRemaining = CountTemporaryFiles();
        var gates = CreateGates(baseline, final, temporaryFilesRemaining);
        var passed = _errors.Count == 0 && gates.All(gate => gate.Passed);

        var summary = new ReliabilitySummary(
            SchemaVersion: "1.0",
            StartedAtUtc: startedAt,
            FinishedAtUtc: DateTimeOffset.UtcNow,
            AppPath: _options.AppPath,
            OutputDirectory: _options.OutputDirectory,
            WarmupCapturesRequested: _options.WarmupCaptures,
            WarmupCapturesCompleted: _warmupCompleted,
            MeasuredCapturesRequested: _options.MeasuredCaptures,
            MeasuredCapturesCompleted: _measuredCompleted,
            ConsoleCyclesRequested: _options.ConsoleCycles,
            ConsoleCyclesCompleted: _consoleCyclesCompleted,
            RegionCancellationCyclesRequested: _options.RegionCancellationCycles,
            RegionCancellationCyclesCompleted: _regionCancellationCyclesCompleted,
            ProcessLifecycleCyclesRequested: _options.ProcessLifecycleCycles,
            ProcessLifecycleCyclesCompleted: _lifecycleResults.Count(
                result => result.Exited && result.ErrorMessage is null),
            CommandFailures: _commandFailures,
            CaptureTimeouts: _captureTimeouts,
            UnexpectedRegionFiles: _unexpectedRegionFiles,
            TemporaryFilesRemaining: temporaryFilesRemaining,
            BaselineResources: baseline,
            FinalResources: final,
            Gates: gates,
            LifecycleCycles: _lifecycleResults,
            Passed: passed,
            Errors: _errors.ToArray(),
            Warnings: _warnings.ToArray());

        WriteReports(summary);
        return summary;
    }

    private void ValidateEnvironment()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "The reliability harness requires Windows.");
        }

        if (!File.Exists(_options.AppPath))
        {
            throw new FileNotFoundException(
                "Build SCapturer in Release before running the harness.",
                _options.AppPath);
        }
    }

    private void PrepareIsolatedSettings()
    {
        var paths = new AppPaths(
            _dataDirectory,
            Path.Combine(_options.OutputDirectory, "legacy-data"));
        var settings = AppSettings.CreateDefault();
        settings.FullCaptureFolder = _fullCaptureDirectory;
        settings.SnipCaptureFolder = _snipCaptureDirectory;
        settings.CopyToClipboard = false;
        settings.PlayCaptureSound = false;
        settings.EnableDiagnostics = true;
        settings.CaptureBackend = CaptureBackendMode.ReferenceGdiPlus;
        new SettingsStore(paths).Save(settings);
    }

    private Process StartPrimary()
    {
        var info = CreateStartInfo("--background");
        var process = Process.Start(info) ??
            throw new InvalidOperationException(
                "Windows did not start the isolated SCapturer process.");
        return process;
    }

    private void WaitForPrimaryReady(Process process)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        var mutexName = $@"Local\SCapturer.App.{_instanceSuffix}";

        while (DateTime.UtcNow < deadline)
        {
            if (process.HasExited)
            {
                throw new InvalidOperationException(
                    $"SCapturer exited during startup with code {process.ExitCode}.");
            }

            if (Mutex.TryOpenExisting(mutexName, out var existingMutex))
            {
                existingMutex?.Dispose();
                break;
            }

            Thread.Sleep(50);
        }

        while (DateTime.UtcNow < deadline)
        {
            if (process.HasExited)
            {
                throw new InvalidOperationException(
                    $"SCapturer exited during startup with code {process.ExitCode}.");
            }

            if (SendCommand("--hide", countFailure: false))
            {
                return;
            }

            Thread.Sleep(150);
        }

        throw new TimeoutException(
            "The isolated SCapturer instance did not expose its IPC endpoint.");
    }

    private void WarmRepresentativeResourcePaths(Process primary)
    {
        const int consoleWarmupCycles = 2;

        for (var iteration = 1; iteration <= consoleWarmupCycles; iteration++)
        {
            if (!SendCommand("--show"))
            {
                throw new InvalidOperationException(
                    $"Console warm-up show command {iteration} failed.");
            }

            Thread.Sleep(150);

            if (!SendCommand("--hide"))
            {
                throw new InvalidOperationException(
                    $"Console warm-up hide command {iteration} failed.");
            }

            Thread.Sleep(150);
        }

        if (!RunRegionCancellation(primary, iteration: 0))
        {
            throw new InvalidOperationException(
                "Representative region-cancellation warm-up failed.");
        }

        Sample(primary, "resource-warmup", 0);
    }

    private bool RunCapture(
        Process primary,
        string phase,
        int iteration)
    {
        var before = CountPngFiles(_fullCaptureDirectory);

        if (!SendCommand("--capture-full"))
        {
            return false;
        }

        var deadline = DateTime.UtcNow +
            TimeSpan.FromSeconds(_options.CaptureTimeoutSeconds);

        while (DateTime.UtcNow < deadline)
        {
            if (primary.HasExited)
            {
                _errors.Add(
                    $"Primary process exited during {phase} capture {iteration}.");
                return false;
            }

            if (CountPngFiles(_fullCaptureDirectory) > before)
            {
                return true;
            }

            Thread.Sleep(50);
        }

        _captureTimeouts++;
        _errors.Add($"Timed out waiting for {phase} capture {iteration}.");
        return false;
    }

    private bool RunRegionCancellation(Process primary, int iteration)
    {
        var before = CountPngFiles(_snipCaptureDirectory);

        if (!SendCommand("--capture-region"))
        {
            return false;
        }

        Thread.Sleep(500);
        var cancellationAccepted = true;

        for (var attempt = 0; attempt < 5; attempt++)
        {
            cancellationAccepted &= SendCommand("--cancel-region");
            Thread.Sleep(300);
        }

        Thread.Sleep(500);

        if (primary.HasExited)
        {
            _errors.Add(
                $"Primary process exited during region cancellation {iteration}.");
            return false;
        }

        var after = CountPngFiles(_snipCaptureDirectory);
        if (after > before)
        {
            _unexpectedRegionFiles += after - before;
            _errors.Add(
                $"Region cancellation {iteration} unexpectedly produced a PNG.");
            return false;
        }

        return cancellationAccepted;
    }

    private bool SendCommand(
        string argument,
        bool countFailure = true)
    {
        using var process = Process.Start(CreateStartInfo(argument));
        if (process is null)
        {
            if (countFailure)
            {
                _commandFailures++;
            }

            return false;
        }

        if (!process.WaitForExit(5000))
        {
            TryKill(process);

            if (countFailure)
            {
                _commandFailures++;
            }

            return false;
        }

        var success = process.ExitCode == 0;
        if (!success && countFailure)
        {
            _commandFailures++;
        }

        return success;
    }

    private ProcessStartInfo CreateStartInfo(string argument)
    {
        var info = new ProcessStartInfo
        {
            FileName = _options.AppPath,
            Arguments = argument,
            WorkingDirectory = Path.GetDirectoryName(_options.AppPath) ??
                Environment.CurrentDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var pair in _environment)
        {
            info.Environment[pair.Key] = pair.Value;
        }

        return info;
    }

    private ResourceSample Sample(
        Process process,
        string phase,
        int iteration)
    {
        var sample = ProcessResourceSampler.Capture(
            process,
            phase,
            iteration);
        _samples.Add(sample);
        File.AppendAllText(
            _resourceSamplesPath,
            JsonSerializer.Serialize(sample, JsonLineOptions) +
            Environment.NewLine);
        return sample;
    }

    private void StopPrimary(Process? primary)
    {
        if (primary is null)
        {
            return;
        }

        try
        {
            if (!primary.HasExited)
            {
                _ = SendCommand("--exit", countFailure: false);

                if (!primary.WaitForExit(15_000))
                {
                    _errors.Add(
                        "Primary process did not exit gracefully within 15 seconds.");
                    TryKill(primary);
                }
            }
        }
        finally
        {
            primary.Dispose();
        }
    }

    private void RunRepeatedProcessLifecycle()
    {
        for (var iteration = 1;
             iteration <= _options.ProcessLifecycleCycles;
             iteration++)
        {
            var stopwatch = Stopwatch.StartNew();
            Process? process = null;
            var started = false;
            var showAccepted = false;
            var hideAccepted = false;
            var exitAccepted = false;
            var exited = false;
            string? error = null;

            try
            {
                process = StartPrimary();
                started = true;
                WaitForPrimaryReady(process);
                showAccepted = SendCommand("--show");
                hideAccepted = SendCommand("--hide");
                exitAccepted = SendCommand("--exit");
                exited = process.WaitForExit(15_000);

                if (!exited)
                {
                    error = "Process did not exit within 15 seconds.";
                    TryKill(process);
                }
                else if (process.ExitCode != 0)
                {
                    error = $"Process exited with code {process.ExitCode}.";
                }
            }
            catch (Exception exception)
            {
                error = exception.GetBaseException().Message;
                if (process is not null)
                {
                    TryKill(process);
                }
            }
            finally
            {
                process?.Dispose();
            }

            if (error is not null)
            {
                _errors.Add($"Lifecycle cycle {iteration}: {error}");
            }

            _lifecycleResults.Add(new LifecycleCycleResult(
                iteration,
                started,
                showAccepted,
                hideAccepted,
                exitAccepted,
                exited,
                stopwatch.Elapsed.TotalMilliseconds,
                error));
        }
    }

    private IReadOnlyList<ReliabilityGate> CreateGates(
        ResourceSample? baseline,
        ResourceSample? final,
        int temporaryFilesRemaining)
    {
        var gates = new List<ReliabilityGate>
        {
            Gate(
                "Warm-up captures",
                _warmupCompleted == _options.WarmupCaptures,
                $"{_warmupCompleted}/{_options.WarmupCaptures}",
                "all complete"),
            Gate(
                "Measured captures",
                _measuredCompleted == _options.MeasuredCaptures,
                $"{_measuredCompleted}/{_options.MeasuredCaptures}",
                "all complete"),
            Gate(
                "Console lifecycle",
                _consoleCyclesCompleted == _options.ConsoleCycles,
                $"{_consoleCyclesCompleted}/{_options.ConsoleCycles}",
                "all complete"),
            Gate(
                "Region cancellation",
                _regionCancellationCyclesCompleted ==
                    _options.RegionCancellationCycles,
                $"{_regionCancellationCyclesCompleted}/" +
                    _options.RegionCancellationCycles,
                "all complete"),
            Gate(
                "Repeated process lifecycle",
                _lifecycleResults.Count(result =>
                    result.Exited && result.ErrorMessage is null) ==
                    _options.ProcessLifecycleCycles,
                _lifecycleResults.Count(result =>
                    result.Exited && result.ErrorMessage is null) +
                    $"/{_options.ProcessLifecycleCycles}",
                "all complete"),
            Gate(
                "IPC command failures",
                _commandFailures == 0,
                _commandFailures.ToString(),
                "0"),
            Gate(
                "Capture timeouts",
                _captureTimeouts == 0,
                _captureTimeouts.ToString(),
                "0"),
            Gate(
                "Unexpected region PNG files",
                _unexpectedRegionFiles == 0,
                _unexpectedRegionFiles.ToString(),
                "0"),
            Gate(
                "Temporary files",
                temporaryFilesRemaining == 0,
                temporaryFilesRemaining.ToString(),
                "0"),
        };

        if (baseline is null || final is null)
        {
            gates.Add(Gate(
                "Resource samples",
                passed: false,
                actual: "missing",
                limit: "baseline and final required"));
            return gates;
        }

        AddDeltaGate(
            gates,
            "GDI objects",
            baseline.GdiObjects,
            final.GdiObjects,
            absoluteLimit: 8);
        AddDeltaGate(
            gates,
            "USER objects",
            baseline.UserObjects,
            final.UserObjects,
            absoluteLimit: 8);
        AddDeltaGate(
            gates,
            "Process handles",
            baseline.HandleCount,
            final.HandleCount,
            absoluteLimit: 32);
        AddDeltaGate(
            gates,
            "Threads",
            baseline.ThreadCount,
            final.ThreadCount,
            absoluteLimit: 3);
        AddMemoryGate(
            gates,
            "Private memory",
            baseline.PrivateMemoryBytes,
            final.PrivateMemoryBytes,
            absoluteLimitBytes: 48L * 1024 * 1024,
            relativeLimit: 0.20);
        AddMemoryGate(
            gates,
            "Working set",
            baseline.WorkingSetBytes,
            final.WorkingSetBytes,
            absoluteLimitBytes: 64L * 1024 * 1024,
            relativeLimit: 0.30);

        return gates;
    }

    private static void AddDeltaGate(
        ICollection<ReliabilityGate> gates,
        string name,
        long baseline,
        long final,
        long absoluteLimit)
    {
        var delta = final - baseline;
        gates.Add(Gate(
            name,
            delta <= absoluteLimit,
            $"baseline {baseline}; final {final}; delta {delta:+#;-#;0}",
            $"delta <= {absoluteLimit}"));
    }

    private static void AddMemoryGate(
        ICollection<ReliabilityGate> gates,
        string name,
        long baseline,
        long final,
        long absoluteLimitBytes,
        double relativeLimit)
    {
        var delta = final - baseline;
        var allowed = Math.Max(
            absoluteLimitBytes,
            (long)(baseline * relativeLimit));

        gates.Add(Gate(
            name,
            delta <= allowed,
            $"baseline {FormatBytes(baseline)}; final {FormatBytes(final)}; " +
                $"delta {FormatSignedBytes(delta)}",
            $"delta <= {FormatBytes(allowed)}"));
    }

    private static ReliabilityGate Gate(
        string name,
        bool passed,
        string actual,
        string limit)
    {
        return new ReliabilityGate(name, passed, actual, limit);
    }

    private int CountTemporaryFiles()
    {
        return Directory.EnumerateFiles(
                _options.OutputDirectory,
                "*.scapturer.tmp",
                SearchOption.AllDirectories)
            .Count();
    }

    private static int CountPngFiles(string directory)
    {
        return Directory.Exists(directory)
            ? Directory.EnumerateFiles(
                directory,
                "*.png",
                SearchOption.TopDirectoryOnly).Count()
            : 0;
    }

    private void WriteReports(ReliabilitySummary summary)
    {
        File.WriteAllText(
            Path.Combine(_options.OutputDirectory, "soak-summary.json"),
            JsonSerializer.Serialize(summary, JsonOptions));
        File.WriteAllText(
            Path.Combine(_options.OutputDirectory, "reliability-report.md"),
            CreateMarkdownReport(summary));
    }

    private static string CreateMarkdownReport(ReliabilitySummary summary)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# SCapturer reliability report");
        builder.AppendLine();
        builder.AppendLine($"**Result:** {(summary.Passed ? "PASS" : "FAIL")}");
        builder.AppendLine();
        builder.AppendLine($"- Started: `{summary.StartedAtUtc:O}`");
        builder.AppendLine($"- Finished: `{summary.FinishedAtUtc:O}`");
        builder.AppendLine($"- Application: `{summary.AppPath}`");
        builder.AppendLine();
        builder.AppendLine("## Workload");
        builder.AppendLine();
        builder.AppendLine(
            $"- Warm-up captures: {summary.WarmupCapturesCompleted}/" +
            summary.WarmupCapturesRequested);
        builder.AppendLine(
            $"- Measured captures: {summary.MeasuredCapturesCompleted}/" +
            summary.MeasuredCapturesRequested);
        builder.AppendLine(
            $"- Console show/hide cycles: {summary.ConsoleCyclesCompleted}/" +
            summary.ConsoleCyclesRequested);
        builder.AppendLine(
            $"- Region cancellations: {summary.RegionCancellationCyclesCompleted}/" +
            summary.RegionCancellationCyclesRequested);
        builder.AppendLine(
            $"- Process lifecycle cycles: {summary.ProcessLifecycleCyclesCompleted}/" +
            summary.ProcessLifecycleCyclesRequested);
        builder.AppendLine();
        builder.AppendLine("## Gates");
        builder.AppendLine();
        builder.AppendLine("| Gate | Result | Actual | Limit |");
        builder.AppendLine("| --- | --- | --- | --- |");

        foreach (var gate in summary.Gates)
        {
            builder.AppendLine(
                $"| {Escape(gate.Name)} | {(gate.Passed ? "PASS" : "FAIL")} | " +
                $"{Escape(gate.Actual)} | {Escape(gate.Limit)} |");
        }

        if (summary.Errors.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Errors");
            builder.AppendLine();
            foreach (var error in summary.Errors)
            {
                builder.AppendLine($"- {error}");
            }
        }

        if (summary.Warnings.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Warnings");
            builder.AppendLine();
            foreach (var warning in summary.Warnings)
            {
                builder.AppendLine($"- {warning}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("Raw resource samples are stored in `resource-samples.jsonl`.");
        return builder.ToString();
    }

    private static string Escape(string value)
    {
        return value.Replace("|", "\\|");
    }

    private static string FormatSignedBytes(long bytes)
    {
        return bytes >= 0
            ? "+" + FormatBytes(bytes)
            : "-" + FormatBytes(Math.Abs(bytes));
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double value = bytes;
        var unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
        catch (Exception exception)
            when (exception is InvalidOperationException or
                  System.ComponentModel.Win32Exception)
        {
            // Final cleanup is best effort.
        }
    }
}
