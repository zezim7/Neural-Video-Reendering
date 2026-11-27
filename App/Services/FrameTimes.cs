using System.IO;

namespace VideoNR.Services;

// Source presentation timestamps in Media Foundation's 100 ns time base.
internal sealed class FrameTimes(IEnumerable<long> samples)
{
    private readonly long[] ticks = samples.Distinct().Order().ToArray();

    internal double AtOrBefore(double seconds)
    {
        if (ticks.Length == 0) throw new InvalidOperationException("Nenhum quadro de vídeo encontrado.");
        var i = Array.BinarySearch(ticks, ToTicks(seconds));
        return Seconds(ticks[i >= 0 ? i : Math.Max(0, ~i - 1)]);
    }

    internal double? Adjacent(double seconds, int direction)
    {
        var i = Array.BinarySearch(ticks, ToTicks(seconds));
        i = i >= 0 ? i + direction : direction > 0 ? ~i : ~i - 1;
        return i >= 0 && i < ticks.Length ? Seconds(ticks[i]) : null;
    }

    private static long ToTicks(double seconds) => (long)Math.Round(seconds * TimeSpan.TicksPerSecond);
    private static double Seconds(long ticks) => (double)ticks / TimeSpan.TicksPerSecond;
}

internal sealed class FrameTimesCache(
    Func<string, CancellationToken, FrameTimes> reader,
    int maxEntries = 8)
{
    private readonly record struct FileStamp(long Length, long LastWriteTicks);
    private sealed record CacheEntry(FileStamp Stamp, Task<FrameTimes> Task, LinkedListNode<string> Node);

    private readonly int capacity = Math.Max(1, maxEntries);
    private readonly Dictionary<string, CacheEntry> entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> lru = new();
    private readonly object gate = new();

    internal Task<FrameTimes> GetOrReadAsync(string path, CancellationToken lifetimeToken)
    {
        var key = NormalizeKey(path);
        var stamp = ReadStamp(key);
        lock (gate)
        {
            if (entries.TryGetValue(key, out var existing))
            {
                if (existing.Stamp == stamp && !existing.Task.IsFaulted && !existing.Task.IsCanceled)
                {
                    lru.Remove(existing.Node);
                    lru.AddFirst(existing.Node);
                    return existing.Task;
                }
                lru.Remove(existing.Node);
                entries.Remove(key);
            }

            while (entries.Count >= capacity && lru.Last is { } oldest)
            {
                lru.RemoveLast();
                entries.Remove(oldest.Value);
            }

            var node = lru.AddFirst(key);
            var task = Task.Run(() => reader(path, lifetimeToken), lifetimeToken);
            entries[key] = new CacheEntry(stamp, task, node);
            return task;
        }
    }

    private static string NormalizeKey(string path)
    {
        try { return Path.GetFullPath(path); }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }
    }

    private static FileStamp ReadStamp(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? new FileStamp(info.Length, info.LastWriteTimeUtc.Ticks) : default;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return default;
        }
    }
}

