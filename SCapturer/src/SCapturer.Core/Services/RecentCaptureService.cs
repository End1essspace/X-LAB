using SCapturer.Core.Models;

namespace SCapturer.Core.Services;

public sealed class RecentCaptureService
{
    public IReadOnlyList<RecentCaptureItem> Load(
        AppSettings settings,
        int maximumCount = 20)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (maximumCount <= 0)
        {
            return Array.Empty<RecentCaptureItem>();
        }

        var queue = new PriorityQueue<RecentCaptureItem, long>();
        var normalizedFullFolder = NormalizeFolder(settings.FullCaptureFolder);
        var normalizedSnipFolder = NormalizeFolder(settings.SnipCaptureFolder);

        var sameFolder = string.Equals(
            normalizedFullFolder,
            normalizedSnipFolder,
            StringComparison.OrdinalIgnoreCase);

        AddFolder(
            queue,
            normalizedFullFolder,
            sameFolder ? null : CaptureKind.FullDesktop,
            maximumCount);

        if (!sameFolder)
        {
            AddFolder(
                queue,
                normalizedSnipFolder,
                CaptureKind.Region,
                maximumCount);
        }

        return queue.UnorderedItems
            .Select(item => item.Element)
            .OrderByDescending(item => item.LastWriteTime)
            .ThenBy(item => item.FileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public RecentCaptureItem? FromResult(CaptureResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        try
        {
            var info = new FileInfo(result.FilePath);
            if (!info.Exists)
            {
                return null;
            }

            return new RecentCaptureItem(
                info.FullName,
                result.Kind,
                info.LastWriteTimeUtc,
                info.Length);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void AddFolder(
        PriorityQueue<RecentCaptureItem, long> queue,
        string folder,
        CaptureKind? configuredKind,
        int maximumCount)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return;
        }

        try
        {
            foreach (var filePath in Directory.EnumerateFiles(
                         folder,
                         "*.png",
                         SearchOption.TopDirectoryOnly))
            {
                try
                {
                    var info = new FileInfo(filePath);
                    if (!info.Exists)
                    {
                        continue;
                    }

                    var kind = configuredKind ??
                        (info.Name.StartsWith(
                            "Snip_",
                            StringComparison.OrdinalIgnoreCase)
                            ? CaptureKind.Region
                            : CaptureKind.FullDesktop);

                    var item = new RecentCaptureItem(
                        info.FullName,
                        kind,
                        info.LastWriteTimeUtc,
                        info.Length);

                    queue.Enqueue(item, item.LastWriteTime.UtcDateTime.Ticks);

                    if (queue.Count > maximumCount)
                    {
                        queue.Dequeue();
                    }
                }
                catch (IOException)
                {
                    // A file may disappear while the recent list is being read.
                }
                catch (UnauthorizedAccessException)
                {
                    // Skip files Windows does not allow the process to inspect.
                }
            }
        }
        catch (IOException)
        {
            // A capture folder may be temporarily unavailable.
        }
        catch (UnauthorizedAccessException)
        {
            // The UI should remain usable even if a folder cannot be enumerated.
        }
    }

    private static string NormalizeFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return string.Empty;
        }

        try
        {
            return Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(folder));
        }
        catch (Exception exception)
            when (exception is ArgumentException or
                  NotSupportedException or
                  PathTooLongException)
        {
            return folder;
        }
    }
}
