using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;
using SCapturer.App.Lifecycle;
using SCapturer.Core.Benchmarking;
using SCapturer.Core.Capture;
using SCapturer.Core.Diagnostics;
using SCapturer.Core.Display;
using SCapturer.Core.Models;
using SCapturer.Core.Persistence;
using SCapturer.Core.Pipeline;
using SCapturer.Core.Services;

namespace SCapturer.Tests;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        var tests = new (string Name, Action Body)[]
        {
            ("Hotkey parser round-trip", HotkeyParserRoundTrip),
            ("Hotkey duplicate rejection", HotkeyDuplicateRejection),
            ("Settings snapshot deep copy", SettingsSnapshotDeepCopy),
            ("Launch option parsing", LaunchOptionParsing),
            ("Launch option validation", LaunchOptionValidation),
            ("Benchmark statistics", BenchmarkStatisticsCalculation),
            ("Explicit app paths", ExplicitAppPaths),
            ("Invalid settings backup", InvalidSettingsBackup),
            ("Duplicate settings normalization", DuplicateSettingsNormalization),
            ("Atomic persistence collision handling", PersistenceCollisionHandling),
            ("Atomic persistence failure cleanup", PersistenceFailureCleanup),
            ("Recent capture ordering and bound", RecentCaptureOrdering),
            ("Autostart command construction", AutostartCommandConstruction),
            ("Pipeline work state", PipelineWorkState),
        };

        var suiteStarted = Stopwatch.StartNew();
        var failures = new List<string>();

        Console.WriteLine($"SCapturer automated tests · {tests.Length} cases");
        Console.WriteLine(new string('─', 72));

        foreach (var test in tests)
        {
            var started = Stopwatch.StartNew();

            try
            {
                test.Body();
                Console.WriteLine($"PASS  {test.Name,-48} {started.ElapsedMilliseconds,5} ms");
            }
            catch (Exception exception)
            {
                failures.Add(test.Name);
                Console.WriteLine($"FAIL  {test.Name,-48} {started.ElapsedMilliseconds,5} ms");
                Console.WriteLine($"      {exception.GetBaseException().Message}");
            }
        }

        Console.WriteLine(new string('─', 72));
        Console.WriteLine(
            $"Completed in {suiteStarted.Elapsed.TotalSeconds:0.00}s · " +
            $"passed {tests.Length - failures.Count} · failed {failures.Count}");

        if (failures.Count == 0)
        {
            return 0;
        }

        Console.WriteLine("Failed cases: " + string.Join(", ", failures));
        return 1;
    }

    private static void HotkeyParserRoundTrip()
    {
        Assert.True(HotkeyBindingService.TryParse(
            "Ctrl+Alt+PrintScreen",
            out var binding,
            out var error), error);
        Assert.Equal("Ctrl+Alt+PrintScreen", HotkeyBindingService.Format(binding));
    }

    private static void HotkeyDuplicateRejection()
    {
        var duplicate = HotkeyBinding.CreateDefaultFullCapture();
        var set = new HotkeyBindingSet(
            duplicate,
            duplicate.CreateSnapshot(),
            HotkeyBinding.CreateDefaultExit(),
            HotkeyBinding.CreateDefaultToggleConsole());

        Assert.False(HotkeyBindingService.TryValidateSet(set, out var error));
        Assert.Contains("same hotkey", error);
    }

    private static void SettingsSnapshotDeepCopy()
    {
        var settings = AppSettings.CreateDefault();
        var snapshot = settings.CreateSnapshot();
        snapshot.FullCaptureHotkey.VirtualKey = 'X';
        snapshot.FullCaptureFolder = @"C:\isolated";

        Assert.Equal((int)'G', settings.FullCaptureHotkey.VirtualKey);
        Assert.NotEqual(snapshot.FullCaptureFolder, settings.FullCaptureFolder);
    }

    private static void LaunchOptionParsing()
    {
        var background = AppLaunchOptions.Parse(["--background"]);
        Assert.True(background.IsValid);
        Assert.True(background.StartHidden);
        Assert.Equal(AppInstanceCommand.None, background.SecondaryCommand);

        var cancel = AppLaunchOptions.Parse(["--cancel-region"]);
        Assert.True(cancel.IsValid);
        Assert.Equal(AppInstanceCommand.CancelRegion, cancel.PrimaryCommand);
        Assert.Equal(AppInstanceCommand.CancelRegion, cancel.SecondaryCommand);

        var resume = AppLaunchOptions.Parse(["--resume-background=4242"]);
        Assert.True(resume.IsValid);
        Assert.True(resume.StartHidden);
        Assert.Equal(4242, resume.ResumeAfterProcessId);
        Assert.Equal(AppInstanceCommand.None, resume.PrimaryCommand);
        Assert.Equal(AppInstanceCommand.None, resume.SecondaryCommand);
    }

    private static void LaunchOptionValidation()
    {
        var invalid = AppLaunchOptions.Parse(["--show", "--hide"]);
        Assert.False(invalid.IsValid);
        Assert.Contains("one command-line", invalid.ErrorMessage ?? string.Empty);

        var invalidResume = AppLaunchOptions.Parse(["--resume-background=invalid"]);
        Assert.False(invalidResume.IsValid);
        Assert.Contains(
            "process identifier",
            invalidResume.ErrorMessage ?? string.Empty);
    }

    private static void BenchmarkStatisticsCalculation()
    {
        var samples = Enumerable.Range(1, 10)
            .Select(iteration => new BenchmarkSample(
                iteration,
                Width: 100,
                Height: 100,
                FileSizeBytes: iteration * 100,
                BackendName: "Fake",
                Metrics: CreateMetrics(
                    total: iteration,
                    pixel: iteration / 2d,
                    png: iteration / 4d,
                    allocations: iteration * 1000)))
            .ToArray();

        var summary = BenchmarkStatistics.CreateSummary(samples);

        Assert.Equal(5.5, summary.MedianTotalMilliseconds, precision: 3);
        Assert.Equal(10d, summary.P95TotalMilliseconds, precision: 3);
        Assert.Equal(5500L, summary.AverageManagedAllocatedBytes);
        Assert.Equal(550L, summary.AverageFileSizeBytes);
    }

    private static void ExplicitAppPaths()
    {
        using var temporary = new TemporaryDirectory();
        var paths = new AppPaths(
            Path.Combine(temporary.Path, "data"),
            Path.Combine(temporary.Path, "legacy"));

        Assert.Equal(
            Path.Combine(temporary.Path, "data", "config.json"),
            paths.SettingsFile);
        Assert.Equal(
            Path.Combine(temporary.Path, "data", "diagnostics"),
            paths.DiagnosticsDirectory);
    }

    private static void InvalidSettingsBackup()
    {
        using var temporary = new TemporaryDirectory();
        var paths = new AppPaths(
            Path.Combine(temporary.Path, "data"),
            Path.Combine(temporary.Path, "legacy"));
        Directory.CreateDirectory(paths.DataDirectory);
        File.WriteAllText(paths.SettingsFile, "{ invalid json");

        var loaded = new SettingsStore(paths).Load();

        Assert.NotNull(loaded);
        Assert.True(File.Exists(paths.SettingsFile));
        Assert.Equal(
            1,
            Directory.EnumerateFiles(
                paths.DataDirectory,
                "config.invalid-*.json").Count());
    }

    private static void DuplicateSettingsNormalization()
    {
        using var temporary = new TemporaryDirectory();
        var paths = new AppPaths(
            Path.Combine(temporary.Path, "data"),
            Path.Combine(temporary.Path, "legacy"));
        Directory.CreateDirectory(paths.DataDirectory);

        var settings = AppSettings.CreateDefault();
        settings.RegionCaptureHotkey = settings.FullCaptureHotkey.CreateSnapshot();
        File.WriteAllText(
            paths.SettingsFile,
            JsonSerializer.Serialize(settings));

        var loaded = new SettingsStore(paths).Load();

        Assert.False(HotkeyBindingService.AreEquivalent(
            loaded.FullCaptureHotkey,
            loaded.RegionCaptureHotkey));
        Assert.Equal((int)'S', loaded.RegionCaptureHotkey.VirtualKey);
    }

    private static void PersistenceCollisionHandling()
    {
        using var temporary = new TemporaryDirectory();
        var stalePath = Path.Combine(
            temporary.Path,
            ".abandoned.scapturer.tmp");
        File.WriteAllText(stalePath, "stale");
        File.SetLastWriteTimeUtc(stalePath, DateTime.UtcNow.AddDays(-2));

        var persistence = new CapturePersistenceService();
        _ = persistence.PrepareDestination(
            temporary.Path,
            CaptureKind.FullDesktop,
            "Probe");
        Assert.False(File.Exists(stalePath));

        using var frame = new FakeCaptureFrame(4, 4);
        var backend = new FakeBackend();
        var firstTemporary = Path.Combine(
            temporary.Path,
            ".fixed.first.scapturer.tmp");
        var secondTemporary = Path.Combine(
            temporary.Path,
            ".fixed.second.scapturer.tmp");

        var first = persistence.PersistPng(
            backend,
            frame,
            new CaptureDestination(
                temporary.Path,
                "fixed",
                firstTemporary,
                Warning: null));
        var second = persistence.PersistPng(
            backend,
            frame,
            new CaptureDestination(
                temporary.Path,
                "fixed",
                secondTemporary,
                Warning: null));

        Assert.Equal("fixed.png", Path.GetFileName(first.FilePath));
        Assert.Equal("fixed_1.png", Path.GetFileName(second.FilePath));
        Assert.True(first.FileSizeBytes > 0);
        Assert.True(second.FileSizeBytes > 0);
        Assert.Empty(Directory.EnumerateFiles(
            temporary.Path,
            "*.scapturer.tmp"));
    }

    private static void PersistenceFailureCleanup()
    {
        using var temporary = new TemporaryDirectory();
        using var frame = new FakeCaptureFrame(2, 2);
        var temporaryPath = Path.Combine(
            temporary.Path,
            ".failure.scapturer.tmp");
        var destination = new CaptureDestination(
            temporary.Path,
            "failure",
            temporaryPath,
            Warning: null);

        Assert.Throws<IOException>(() =>
            new CapturePersistenceService().PersistPng(
                new FailingBackend(),
                frame,
                destination));
        Assert.False(File.Exists(temporaryPath));
        Assert.False(File.Exists(Path.Combine(temporary.Path, "failure.png")));
    }

    private static void RecentCaptureOrdering()
    {
        using var temporary = new TemporaryDirectory();
        var full = Path.Combine(temporary.Path, "Full");
        var snips = Path.Combine(temporary.Path, "Snips");
        Directory.CreateDirectory(full);
        Directory.CreateDirectory(snips);

        CreatePngPlaceholder(Path.Combine(full, "Screenshot_old.png"), -3);
        CreatePngPlaceholder(Path.Combine(snips, "Snip_mid.png"), -2);
        CreatePngPlaceholder(Path.Combine(full, "Screenshot_new.png"), -1);

        var settings = AppSettings.CreateDefault();
        settings.FullCaptureFolder = full;
        settings.SnipCaptureFolder = snips;

        var items = new RecentCaptureService().Load(settings, maximumCount: 2);

        Assert.Equal(2, items.Count);
        Assert.Equal("Screenshot_new.png", items[0].FileName);
        Assert.Equal("Snip_mid.png", items[1].FileName);
        Assert.Equal(CaptureKind.Region, items[1].Kind);
    }

    private static void AutostartCommandConstruction()
    {
        Assert.Equal(
            "\"C:\\Apps\\SCapturer.exe\" --background",
            AutostartService.CreateCommand(
                @"C:\Apps\SCapturer.exe",
                entryAssemblyLocation: null));

        Assert.Equal(
            "\"C:\\Program Files\\dotnet\\dotnet.exe\" " +
            "\"C:\\Repo\\SCapturer.dll\" --background",
            AutostartService.CreateCommand(
                @"C:\Program Files\dotnet\dotnet.exe",
                @"C:\Repo\SCapturer.dll"));
    }

    private static void PipelineWorkState()
    {
        Assert.False(CapturePipelineSnapshot.Initial.HasWork);

        var active = new CapturePipelineSnapshot(
            Version: 1,
            State: CapturePipelineState.Capturing,
            HasActiveRequest: true,
            HasPendingRequest: false,
            ActiveKind: CaptureKind.FullDesktop,
            PendingKind: null,
            ActiveTrigger: "Test",
            PendingTrigger: null);

        Assert.True(active.HasWork);
    }

    private static CaptureMetrics CreateMetrics(
        double total,
        double pixel,
        double png,
        long allocations)
    {
        return new CaptureMetrics(
            StartedAtUtc: DateTimeOffset.UtcNow,
            Trigger: "Test",
            DispatchMilliseconds: 0,
            DirectoryPreparationMilliseconds: 0,
            BitmapAllocationMilliseconds: 0,
            PixelAcquisitionMilliseconds: pixel,
            PngPersistenceMilliseconds: png,
            ClipboardMilliseconds: 0,
            SoundMilliseconds: 0,
            TotalMilliseconds: total,
            ManagedAllocatedBytes: allocations,
            WorkingSetBeforeBytes: 0,
            WorkingSetAfterBytes: 0);
    }

    private static void CreatePngPlaceholder(string path, int minutesOffset)
    {
        File.WriteAllBytes(path, [0x89, 0x50, 0x4E, 0x47]);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(minutesOffset));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "SCapturer.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
                // Test cleanup is best effort.
            }
            catch (UnauthorizedAccessException)
            {
                // Test cleanup is best effort.
            }
        }
    }

    private sealed class FakeCaptureFrame : CaptureFrame
    {
        private Bitmap? _bitmap;

        public FakeCaptureFrame(int width, int height)
        {
            _bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using var graphics = Graphics.FromImage(_bitmap);
            graphics.Clear(Color.CornflowerBlue);
        }

        public override int Width => Bitmap.Width;

        public override int Height => Bitmap.Height;

        public override int Stride => checked(Width * 4);

        public override Bitmap Bitmap => _bitmap ??
            throw new ObjectDisposedException(nameof(FakeCaptureFrame));

        public override CaptureBackendKind BackendKind =>
            CaptureBackendKind.ReferenceGdiPlus;

        public override string BackendName => "Fake";

        public override void Dispose()
        {
            Interlocked.Exchange(ref _bitmap, null)?.Dispose();
        }
    }

    private class FakeBackend : ICaptureBackend
    {
        public CaptureBackendKind Kind => CaptureBackendKind.ReferenceGdiPlus;

        public string Name => "Fake";

        public bool IsAvailable(out string? reason)
        {
            reason = null;
            return true;
        }

        public CaptureBackendCaptureResult Capture(
            PhysicalRectangle bounds,
            Action<CaptureBackendPhase>? phaseChanged = null)
        {
            throw new NotSupportedException();
        }

        public CaptureFrame Crop(CaptureFrame source, Rectangle region)
        {
            throw new NotSupportedException();
        }

        public virtual void SavePng(CaptureFrame frame, string filePath)
        {
            frame.Bitmap.Save(filePath, ImageFormat.Png);
        }
    }

    private sealed class FailingBackend : FakeBackend
    {
        public override void SavePng(CaptureFrame frame, string filePath)
        {
            File.WriteAllText(filePath, "partial");
            throw new IOException("Synthetic encoder failure.");
        }
    }

    private static class Assert
    {
        public static void True(bool condition, string? message = null)
        {
            if (!condition)
            {
                throw new InvalidOperationException(
                    message ?? "Expected condition to be true.");
            }
        }

        public static void False(bool condition, string? message = null)
        {
            True(!condition, message ?? "Expected condition to be false.");
        }

        public static void Equal<T>(T expected, T actual)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    $"Expected '{expected}', actual '{actual}'.");
            }
        }

        public static void Equal(
            double expected,
            double actual,
            int precision)
        {
            var scale = Math.Pow(10, precision);
            if (Math.Round(expected * scale) != Math.Round(actual * scale))
            {
                throw new InvalidOperationException(
                    $"Expected '{expected}', actual '{actual}'.");
            }
        }

        public static void NotEqual<T>(T notExpected, T actual)
        {
            if (EqualityComparer<T>.Default.Equals(notExpected, actual))
            {
                throw new InvalidOperationException(
                    $"Did not expect '{actual}'.");
            }
        }

        public static void NotNull(object? value)
        {
            if (value is null)
            {
                throw new InvalidOperationException("Expected a non-null value.");
            }
        }

        public static void Contains(string expected, string actual)
        {
            if (!actual.Contains(expected, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Expected '{actual}' to contain '{expected}'.");
            }
        }

        public static void Empty<T>(IEnumerable<T> values)
        {
            if (values.Any())
            {
                throw new InvalidOperationException("Expected an empty sequence.");
            }
        }

        public static TException Throws<TException>(Action action)
            where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException exception)
            {
                return exception;
            }

            throw new InvalidOperationException(
                $"Expected exception {typeof(TException).Name}.");
        }
    }
}
