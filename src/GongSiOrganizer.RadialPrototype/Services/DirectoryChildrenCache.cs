using System.IO;

namespace GongSiOrganizer.RadialPrototype.Services;

internal sealed record DirectoryChildEntry(string Name, string FullPath);

internal sealed record DirectoryCachePolicy(
    int MaximumParents,
    long MaximumEstimatedBytes,
    TimeSpan Freshness,
    int MaximumConcurrentReads)
{
    public static DirectoryCachePolicy CreateAdaptive(long? availableMemoryBytes = null)
    {
        var available = availableMemoryBytes ?? GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        if (available <= 0) available = 8L * 1024 * 1024 * 1024;

        return available switch
        {
            <= 4L * 1024 * 1024 * 1024 => new(
                256,
                4L * 1024 * 1024,
                TimeSpan.FromMinutes(2),
                2),
            <= 8L * 1024 * 1024 * 1024 => new(
                384,
                8L * 1024 * 1024,
                TimeSpan.FromMinutes(2),
                2),
            <= 16L * 1024 * 1024 * 1024 => new(
                640,
                16L * 1024 * 1024,
                TimeSpan.FromMinutes(2),
                2),
            _ => new(
                1024,
                24L * 1024 * 1024,
                TimeSpan.FromMinutes(2),
                2)
        };
    }
}

/// <summary>
/// Stores only immediate child-folder names. It never recursively scans a
/// volume, limits disk concurrency for HDD/removable media, and evicts by LRU.
/// </summary>
internal sealed class DirectoryChildrenCache : IDisposable
{
    private static readonly HashSet<string> ExcludedNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".SynologyWorkingDirectory",
            "$RECYCLE.BIN",
            "System Volume Information"
        };

    private readonly object _gate = new();
    private readonly Dictionary<string, CacheEntry> _entries =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _readGate;
    private long _estimatedBytes;
    private volatile bool _disposed;

    public DirectoryChildrenCache(DirectoryCachePolicy? policy = null)
    {
        Policy = policy ?? DirectoryCachePolicy.CreateAdaptive();
        _readGate = new SemaphoreSlim(
            Math.Max(1, Policy.MaximumConcurrentReads),
            Math.Max(1, Policy.MaximumConcurrentReads));
    }

    public DirectoryCachePolicy Policy { get; }

    internal int CachedParentCount
    {
        get
        {
            lock (_gate) return _entries.Count;
        }
    }

    internal long EstimatedBytes
    {
        get
        {
            lock (_gate) return _estimatedBytes;
        }
    }

    public Task<IReadOnlyList<DirectoryChildEntry>> GetChildrenAsync(string parentPath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parentPath));
        var now = DateTime.UtcNow;
        CacheEntry entry;
        TaskCompletionSource<IReadOnlyList<DirectoryChildEntry>>? pending = null;

        lock (_gate)
        {
            if (_entries.TryGetValue(normalized, out var existing) &&
                now - existing.CreatedUtc <= Policy.Freshness)
            {
                existing.LastAccessUtc = now;
                return existing.Task;
            }

            if (existing is not null)
            {
                _entries.Remove(normalized);
                _estimatedBytes -= existing.EstimatedBytes;
            }

            pending = new TaskCompletionSource<IReadOnlyList<DirectoryChildEntry>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            entry = new CacheEntry(pending.Task, now);
            _entries[normalized] = entry;
        }

        _ = LoadAndStoreAsync(normalized, entry, pending);
        return entry.Task;
    }

    public void Invalidate(string parentPath)
    {
        if (_disposed || string.IsNullOrWhiteSpace(parentPath)) return;
        string normalized;
        try
        {
            normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parentPath));
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException)
        {
            return;
        }

        lock (_gate)
        {
            if (!_entries.Remove(normalized, out var removed)) return;
            _estimatedBytes -= removed.EstimatedBytes;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
            _estimatedBytes = 0;
        }
    }

    private async Task LoadAndStoreAsync(
        string parentPath,
        CacheEntry entry,
        TaskCompletionSource<IReadOnlyList<DirectoryChildEntry>> pending)
    {
        var cancellationToken = _lifetime.Token;
        try
        {
            await _readGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            IReadOnlyList<DirectoryChildEntry> items;
            try
            {
                items = await Task.Run(
                        () => ReadChildren(parentPath, cancellationToken),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                _readGate.Release();
            }

            pending.TrySetResult(items);
            var estimatedBytes = EstimateBytes(items);
            lock (_gate)
            {
                if (!_entries.TryGetValue(parentPath, out var current) ||
                    !ReferenceEquals(current, entry))
                    return;

                current.EstimatedBytes = estimatedBytes;
                current.LastAccessUtc = DateTime.UtcNow;
                _estimatedBytes += estimatedBytes;
                TrimUnsafe(current);
            }
        }
        catch (OperationCanceledException)
        {
            pending.TrySetCanceled();
        }
        catch (Exception exception)
        {
            pending.TrySetException(exception);
            lock (_gate)
            {
                if (_entries.TryGetValue(parentPath, out var current) &&
                    ReferenceEquals(current, entry))
                    _entries.Remove(parentPath);
            }
        }
    }

    private static IReadOnlyList<DirectoryChildEntry> ReadChildren(
        string parentPath,
        CancellationToken cancellationToken)
    {
        var result = new List<DirectoryChildEntry>();
        foreach (var childPath in Directory.EnumerateDirectories(parentPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = new DirectoryInfo(childPath);
            if (!ShouldInclude(directory)) continue;
            result.Add(new DirectoryChildEntry(directory.Name, directory.FullName));
        }

        return result
            .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static bool ShouldInclude(DirectoryInfo directory)
    {
        if (ExcludedNames.Contains(directory.Name)) return false;
        try
        {
            if ((directory.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0)
                return false;
            if (!string.IsNullOrWhiteSpace(directory.LinkTarget)) return false;
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                System.Security.SecurityException)
        {
            return false;
        }
    }

    private static long EstimateBytes(IReadOnlyList<DirectoryChildEntry> items)
    {
        long bytes = 64 + items.Count * 16L;
        foreach (var item in items)
            bytes += 72 + (item.Name.Length + item.FullPath.Length) * sizeof(char);
        return bytes;
    }

    private void TrimUnsafe(CacheEntry protectedEntry)
    {
        while (_entries.Count > Policy.MaximumParents ||
               _estimatedBytes > Policy.MaximumEstimatedBytes)
        {
            var victim = _entries
                .Where(pair =>
                    !ReferenceEquals(pair.Value, protectedEntry) &&
                    pair.Value.Task.IsCompleted)
                .OrderBy(pair => pair.Value.LastAccessUtc)
                .FirstOrDefault();
            if (string.IsNullOrWhiteSpace(victim.Key))
            {
                // An unusually huge single directory remains available to the
                // active menu but is not retained in the shared cache.
                if (_estimatedBytes > Policy.MaximumEstimatedBytes &&
                    protectedEntry.Task.IsCompleted)
                {
                    var protectedPair = _entries.FirstOrDefault(pair =>
                        ReferenceEquals(pair.Value, protectedEntry));
                    if (!string.IsNullOrWhiteSpace(protectedPair.Key))
                    {
                        _entries.Remove(protectedPair.Key);
                        _estimatedBytes -= protectedEntry.EstimatedBytes;
                    }
                }

                break;
            }

            _entries.Remove(victim.Key);
            _estimatedBytes -= victim.Value.EstimatedBytes;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
        Clear();
        // A worker may still release the semaphore after cancellation.
        // Letting this small managed object be collected avoids that race.
    }

    private sealed class CacheEntry
    {
        public CacheEntry(
            Task<IReadOnlyList<DirectoryChildEntry>> task,
            DateTime createdUtc)
        {
            Task = task;
            CreatedUtc = createdUtc;
            LastAccessUtc = createdUtc;
        }

        public Task<IReadOnlyList<DirectoryChildEntry>> Task { get; }
        public DateTime CreatedUtc { get; }
        public DateTime LastAccessUtc { get; set; }
        public long EstimatedBytes { get; set; }
    }
}
