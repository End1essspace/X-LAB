using System.Collections.Concurrent;
using SCapturer.Core.Capture;
using SCapturer.Core.Models;

namespace SCapturer.Core.Persistence;

public sealed record CaptureDestination(
    string DirectoryPath,
    string BaseFileName,
    string TemporaryFilePath,
    string? Warning);

public sealed record CapturePersistenceResult(
    string FilePath,
    long FileSizeBytes);

public sealed class CapturePersistenceService
{
    private const int ConservativeMaximumPathLength = 240;
    private const long FreeSpaceReserveBytes = 8L * 1024 * 1024;
    private static readonly TimeSpan StaleTemporaryFileAge = TimeSpan.FromHours(24);

    private readonly ConcurrentDictionary<string, byte> _validatedFolders =
        new(StringComparer.OrdinalIgnoreCase);

    public CaptureDestination PrepareDestination(
        string configuredFolder,
        CaptureKind kind,
        string filePrefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePrefix);

        var fallbackFolder = GetDefaultFolder(kind);
        string preferredFolder;
        string? normalizationError = null;

        try
        {
            preferredFolder = NormalizeFolder(configuredFolder);
        }
        catch (Exception exception)
            when (exception is ArgumentException or
                  IOException or
                  NotSupportedException or
                  UnauthorizedAccessException)
        {
            preferredFolder = string.Empty;
            normalizationError = exception.Message;
        }

        CaptureDestination destination;
        var error = string.Empty;

        if (normalizationError is null &&
            TryPrepareFolder(preferredFolder, filePrefix, out destination, out error))
        {
            return destination;
        }

        error = normalizationError ?? error;

        if (string.Equals(
                preferredFolder,
                fallbackFolder,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException(
                $"The capture folder is unavailable: {error}");
        }

        if (!TryPrepareFolder(
                fallbackFolder,
                filePrefix,
                out var fallbackDestination,
                out var fallbackError))
        {
            throw new IOException(
                $"The configured capture folder is unavailable ({error}), and the " +
                $"fallback folder is also unavailable ({fallbackError}).");
        }

        return fallbackDestination with
        {
            Warning =
                $"The configured capture folder was unavailable ({error}). " +
                $"The PNG was saved to the fallback folder: {fallbackFolder}",
        };
    }

    public CapturePersistenceResult PersistPng(
        ICaptureBackend backend,
        CaptureFrame frame,
        CaptureDestination destination)
    {
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(destination);

        EnsureSufficientFreeSpace(destination.DirectoryPath, frame);

        try
        {
            backend.SavePng(frame, destination.TemporaryFilePath);

            var temporaryInfo = new FileInfo(destination.TemporaryFilePath);
            if (!temporaryInfo.Exists || temporaryInfo.Length <= 0)
            {
                throw new IOException(
                    "The PNG encoder did not produce a non-empty temporary file.");
            }

            FlushFileToDisk(destination.TemporaryFilePath);

            var finalPath = CommitUnique(
                destination.TemporaryFilePath,
                destination.DirectoryPath,
                destination.BaseFileName);

            var finalInfo = new FileInfo(finalPath);
            return new CapturePersistenceResult(finalInfo.FullName, finalInfo.Length);
        }
        catch
        {
            TryDelete(destination.TemporaryFilePath);
            throw;
        }
    }

    private bool TryPrepareFolder(
        string folder,
        string prefix,
        out CaptureDestination destination,
        out string error)
    {
        destination = null!;
        error = string.Empty;

        try
        {
            if (string.IsNullOrWhiteSpace(folder))
            {
                throw new ArgumentException("The folder path is empty.");
            }

            Directory.CreateDirectory(folder);
            ValidateWritableFolderOnce(folder);
            CleanupStaleTemporaryFilesOnce(folder);

            var baseFileName = $"{prefix}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss-fff}";
            var temporaryFilePath = Path.Combine(
                folder,
                $".{baseFileName}.{Guid.NewGuid():N}.scapturer.tmp");
            var candidateFinalPath = Path.Combine(folder, baseFileName + ".png");

            EnsureConservativePathLength(temporaryFilePath);
            EnsureConservativePathLength(candidateFinalPath);

            destination = new CaptureDestination(
                folder,
                baseFileName,
                temporaryFilePath,
                Warning: null);

            return true;
        }
        catch (Exception exception)
            when (exception is ArgumentException or
                  IOException or
                  NotSupportedException or
                  UnauthorizedAccessException)
        {
            error = exception.Message;
            return false;
        }
    }

    private void ValidateWritableFolderOnce(string folder)
    {
        if (_validatedFolders.ContainsKey(folder))
        {
            return;
        }

        var probePath = Path.Combine(
            folder,
            $".scapturer-write-probe-{Guid.NewGuid():N}.tmp");

        try
        {
            using var stream = new FileStream(
                probePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1,
                FileOptions.WriteThrough | FileOptions.DeleteOnClose);

            stream.WriteByte(0);
            stream.Flush(flushToDisk: true);
            _validatedFolders.TryAdd(folder, 0);
        }
        finally
        {
            TryDelete(probePath);
        }
    }

    private void CleanupStaleTemporaryFilesOnce(string folder)
    {
        var cleanupKey = folder + "\0cleanup";
        if (!_validatedFolders.TryAdd(cleanupKey, 0))
        {
            return;
        }

        try
        {
            var threshold = DateTime.UtcNow - StaleTemporaryFileAge;

            foreach (var filePath in Directory.EnumerateFiles(
                         folder,
                         "*.scapturer.tmp",
                         SearchOption.TopDirectoryOnly))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(filePath) < threshold)
                    {
                        File.Delete(filePath);
                    }
                }
                catch (IOException)
                {
                    // A live or locked temporary file must be left untouched.
                }
                catch (UnauthorizedAccessException)
                {
                    // Cleanup is best effort and must not block a new capture.
                }
            }
        }
        catch (IOException)
        {
            // Cleanup is best effort and must not block a new capture.
        }
        catch (UnauthorizedAccessException)
        {
            // Cleanup is best effort and must not block a new capture.
        }
    }

    private static string CommitUnique(
        string temporaryFilePath,
        string directory,
        string baseFileName)
    {
        for (var suffix = 0; suffix < 10_000; suffix++)
        {
            var fileName = suffix == 0
                ? baseFileName + ".png"
                : $"{baseFileName}_{suffix}.png";
            var candidate = Path.Combine(directory, fileName);
            EnsureConservativePathLength(candidate);

            try
            {
                File.Move(temporaryFilePath, candidate, overwrite: false);
                return candidate;
            }
            catch (IOException) when (File.Exists(candidate))
            {
                // Another writer claimed this name. Try the next suffix.
            }
        }

        throw new IOException(
            "SCapturer could not allocate a unique final PNG file name.");
    }

    private static void FlushFileToDisk(string filePath)
    {
        using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.WriteThrough);

        stream.Flush(flushToDisk: true);
    }

    private static void EnsureSufficientFreeSpace(
        string directory,
        CaptureFrame frame)
    {
        long requiredBytes;

        try
        {
            requiredBytes = checked(
                (long)frame.Stride * frame.Height + FreeSpaceReserveBytes);
        }
        catch (OverflowException)
        {
            throw new IOException(
                "The capture dimensions are too large for safe persistence.");
        }

        try
        {
            var root = Path.GetPathRoot(directory);
            if (string.IsNullOrWhiteSpace(root) ||
                root.StartsWith(@"\\", StringComparison.Ordinal))
            {
                return;
            }

            var drive = new DriveInfo(root);
            if (!drive.IsReady)
            {
                throw new IOException($"The destination drive is not ready: {root}");
            }

            if (drive.AvailableFreeSpace < requiredBytes)
            {
                throw new IOException(
                    $"Insufficient free space. Required at least " +
                    $"{FormatBytes(requiredBytes)}, available " +
                    $"{FormatBytes(drive.AvailableFreeSpace)}.");
            }
        }
        catch (ArgumentException)
        {
            // Some UNC and provider paths do not map cleanly to DriveInfo.
            // The encoder remains the source of truth for those destinations.
        }
    }

    private static string NormalizeFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return string.Empty;
        }

        var expanded = Environment.ExpandEnvironmentVariables(folder.Trim().Trim('"'));
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(expanded));
    }

    private static string GetDefaultFolder(CaptureKind kind)
    {
        var defaults = AppSettings.CreateDefault();
        return kind == CaptureKind.Region
            ? defaults.SnipCaptureFolder
            : defaults.FullCaptureFolder;
    }

    private static void EnsureConservativePathLength(string path)
    {
        if (path.Length > ConservativeMaximumPathLength)
        {
            throw new PathTooLongException(
                $"The capture path exceeds the conservative Windows limit of " +
                $"{ConservativeMaximumPathLength} characters.");
        }
    }

    private static void TryDelete(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        try
        {
            File.Delete(filePath);
        }
        catch (IOException)
        {
            // Cleanup is best effort.
        }
        catch (UnauthorizedAccessException)
        {
            // Cleanup is best effort.
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }
}
