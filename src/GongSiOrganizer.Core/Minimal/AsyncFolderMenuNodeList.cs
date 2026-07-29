using System.Collections;

namespace GongSiOrganizer.Core;

/// <summary>
/// A non-blocking folder list. Before loading completes it behaves as an empty
/// list, allowing the radial menu to keep rendering and accepting input.
/// </summary>
public sealed class AsyncFolderMenuNodeList : IReadOnlyList<FolderMenuNode>
{
    private readonly object _gate = new();
    private readonly Func<Task<IReadOnlyList<FolderMenuNode>>> _loader;
    private IReadOnlyList<FolderMenuNode> _items = [];
    private Task? _loadTask;
    private bool _isLoading;
    private bool _isLoaded;
    private string? _errorMessage;

    public AsyncFolderMenuNodeList(Func<Task<IReadOnlyList<FolderMenuNode>>> loader)
    {
        ArgumentNullException.ThrowIfNull(loader);
        _loader = loader;
    }

    public event EventHandler? Changed;

    public bool IsLoading
    {
        get
        {
            lock (_gate) return _isLoading;
        }
    }

    public bool IsLoaded
    {
        get
        {
            lock (_gate) return _isLoaded;
        }
    }

    public string? ErrorMessage
    {
        get
        {
            lock (_gate) return _errorMessage;
        }
    }

    public int Count
    {
        get
        {
            lock (_gate) return _items.Count;
        }
    }

    public FolderMenuNode this[int index]
    {
        get
        {
            lock (_gate) return _items[index];
        }
    }

    /// <summary>
    /// Starts loading once and returns true only for the caller that started it.
    /// This method never waits for filesystem I/O.
    /// </summary>
    public bool BeginLoad()
    {
        lock (_gate)
        {
            if (_isLoaded || _isLoading) return false;
            _isLoading = true;
        }

        var task = LoadCoreAsync();
        lock (_gate) _loadTask = task;
        return true;
    }

    public Task EnsureLoadedAsync()
    {
        _ = BeginLoad();
        lock (_gate) return _loadTask ?? Task.CompletedTask;
    }

    public AsyncFolderMenuNodeList Transform(
        Func<IReadOnlyList<FolderMenuNode>, IReadOnlyList<FolderMenuNode>> transform)
    {
        ArgumentNullException.ThrowIfNull(transform);
        return new AsyncFolderMenuNodeList(async () =>
        {
            await EnsureLoadedAsync().ConfigureAwait(false);
            IReadOnlyList<FolderMenuNode> snapshot;
            lock (_gate)
            {
                if (!string.IsNullOrWhiteSpace(_errorMessage))
                    throw new IOException(_errorMessage);
                snapshot = _items;
            }
            return transform(snapshot);
        });
    }

    public IEnumerator<FolderMenuNode> GetEnumerator()
    {
        IReadOnlyList<FolderMenuNode> snapshot;
        lock (_gate) snapshot = _items;
        return snapshot.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private async Task LoadCoreAsync()
    {
        IReadOnlyList<FolderMenuNode> items;
        string? error = null;
        try
        {
            items = await _loader().ConfigureAwait(false) ?? [];
        }
        catch (OperationCanceledException)
        {
            items = [];
            error = "目录读取已取消。";
        }
        catch (Exception exception)
        {
            items = [];
            error = exception.Message;
        }

        lock (_gate)
        {
            _items = items;
            _errorMessage = error;
            _isLoading = false;
            _isLoaded = true;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
