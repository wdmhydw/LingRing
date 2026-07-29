using System.IO;
using GongSiOrganizer.Core;

namespace GongSiOrganizer.RadialPrototype.Services;

internal sealed class DirectoryTreeCache : IDisposable
{
    private readonly string _rootPath;
    private readonly SemaphoreSlim _reloadGate = new(1, 1);
    private readonly DirectoryChildrenCache? _childrenCache;
    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _debounce;
    private IReadOnlyList<DriveDescriptor> _availableDrives = [];
    private volatile bool _disposed;

    public DirectoryTreeCache(string rootPath)
    {
        _rootPath = ArchiveScope.Normalize(rootPath);
        if (ArchiveScope.IsThisPc(_rootPath))
            _childrenCache = new DirectoryChildrenCache();
    }

    public FolderCatalogResult? Snapshot { get; private set; }
    public event EventHandler? Changed;

    public FolderCatalogResult? CreateMenuSnapshot()
    {
        if (!ArchiveScope.IsThisPc(_rootPath)) return Snapshot;
        return CreateThisPcSnapshot(_availableDrives);
    }

    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _reloadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            FolderCatalogResult snapshot;
            if (ArchiveScope.IsThisPc(_rootPath))
            {
                _childrenCache!.Clear();
                var drives = await Task.Run(
                        ReadReadyLocalDrives,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (drives.Count == 0)
                    throw new DirectoryNotFoundException(
                        "没有发现已就绪的本地固定盘或可移动盘。");
                _availableDrives = drives;
                snapshot = CreateThisPcSnapshot(drives);
            }
            else
            {
                snapshot = await Task.Run(
                        () => new FolderCatalog().Scan(_rootPath),
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            if (_disposed) return;
            Snapshot = snapshot;
            Changed?.Invoke(this, EventArgs.Empty);
            EnsureWatcher();
        }
        finally
        {
            _reloadGate.Release();
        }
    }

    private void EnsureWatcher()
    {
        if (_disposed ||
            ArchiveScope.IsThisPc(_rootPath) ||
            _watcher is not null ||
            !Directory.Exists(_rootPath))
            return;
        _watcher = new FileSystemWatcher(_rootPath)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.DirectoryName,
            Filter = "*",
            EnableRaisingEvents = true
        };
        _watcher.Created += OnDirectoryChanged;
        _watcher.Deleted += OnDirectoryChanged;
        _watcher.Renamed += OnDirectoryChanged;
    }

    private FolderCatalogResult CreateThisPcSnapshot(IReadOnlyList<DriveDescriptor> drives)
    {
        var folders = drives
            .Select(drive => new FolderMenuNode(
                drive.DisplayName,
                ArchiveScope.CreateDriveToken(drive.RootPath),
                CreateAsyncChildren(drive.RootPath, drive.RootPath, 0)))
            .ToArray();
        return new FolderCatalogResult(
            ArchiveScope.ThisPcRoot,
            folders,
            folders.Length,
            0);
    }

    private AsyncFolderMenuNodeList CreateAsyncChildren(
        string driveRoot,
        string parentPath,
        int depth)
    {
        return new AsyncFolderMenuNodeList(async () =>
        {
            if (_disposed || depth >= 32) return [];
            var entries = await _childrenCache!
                .GetChildrenAsync(parentPath)
                .ConfigureAwait(false);
            if (_disposed) return [];
            return entries
                .Select(entry => new FolderMenuNode(
                    entry.Name,
                    ArchiveScope.CreateChildToken(
                        entry.FullPath,
                        driveRoot),
                    CreateAsyncChildren(
                        driveRoot,
                        entry.FullPath,
                        depth + 1)))
                .ToArray();
        });
    }

    private static IReadOnlyList<DriveDescriptor> ReadReadyLocalDrives()
    {
        return ArchiveScope.GetReadyLocalDrives()
            .Select(drive => new DriveDescriptor(
                drive.Name,
                GetDriveDisplayName(drive)))
            .ToArray();
    }

    private static string GetDriveDisplayName(DriveInfo drive)
    {
        var letter = drive.Name.TrimEnd(Path.DirectorySeparatorChar);
        try
        {
            return string.IsNullOrWhiteSpace(drive.VolumeLabel)
                ? $"{letter} 本地磁盘"
                : $"{letter} {drive.VolumeLabel}";
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                System.Security.SecurityException)
        {
            return $"{letter} 本地磁盘";
        }
    }

    private void OnDirectoryChanged(object sender, FileSystemEventArgs e)
    {
        if (_disposed) return;
        _debounce?.Cancel();
        _debounce?.Dispose();
        _debounce = new CancellationTokenSource();
        var token = _debounce.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(450, token).ConfigureAwait(false);
                await ReloadAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }, token);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _debounce?.Cancel();
        _debounce?.Dispose();
        _watcher?.Dispose();
        _childrenCache?.Dispose();
        // A scan already running on a worker thread may still release this gate.
        // Leaving this tiny managed object for GC avoids a dispose/release race.
    }

    private sealed record DriveDescriptor(string RootPath, string DisplayName);
}
