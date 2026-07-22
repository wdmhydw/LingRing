using System.IO;
using GongSiOrganizer.Core;

namespace GongSiOrganizer.RadialPrototype.Services;

internal sealed class DirectoryTreeCache : IDisposable
{
    private readonly string _rootPath;
    private readonly SemaphoreSlim _reloadGate = new(1, 1);
    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _debounce;
    private volatile bool _disposed;

    public DirectoryTreeCache(string rootPath)
    {
        _rootPath = rootPath;
    }

    public FolderCatalogResult? Snapshot { get; private set; }
    public event EventHandler? Changed;

    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _reloadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            var snapshot = await Task.Run(
                () => new FolderCatalog().Scan(_rootPath),
                cancellationToken).ConfigureAwait(false);
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
        if (_disposed || _watcher is not null || !Directory.Exists(_rootPath)) return;
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
        // A scan already running on a worker thread may still release this gate.
        // Leaving this tiny managed object for GC avoids a dispose/release race.
    }
}
