using GongSiOrganizer.Core;

namespace GongSiOrganizer.RadialPrototype.Radial;

public sealed record RadialRingView(
    IReadOnlyList<FolderMenuNode?> Slots,
    int TotalCount,
    int Offset,
    string? SelectedRelativePath);

public sealed class RadialMenuSession : IDisposable
{
    private readonly IReadOnlyList<FolderMenuNode> _rootItems;
    private readonly List<FolderMenuNode> _path = [];
    private readonly Dictionary<AsyncFolderMenuNodeList, FolderMenuNode> _observedLists = [];
    private readonly SynchronizationContext? _synchronizationContext;
    private readonly int[] _offsets = [0, 0, 0];
    private int _anchorDepth;
    private bool _outerSelectionHeld;
    private bool _outerPreviewPending;
    private bool _disposed;

    public RadialMenuSession(IReadOnlyList<FolderMenuNode> rootItems)
    {
        _rootItems = rootItems;
        _synchronizationContext = SynchronizationContext.Current;
    }

    public IReadOnlyList<FolderMenuNode> Path => _path;
    public FolderMenuNode? CurrentDestination => _path.LastOrDefault();
    public bool IsCurrentChildrenLoading =>
        CurrentDestination?.Children is AsyncFolderMenuNodeList { IsLoading: true };
    public string? CurrentChildrenError =>
        (CurrentDestination?.Children as AsyncFolderMenuNodeList)?.ErrorMessage;

    public event EventHandler? Changed;

    public string Breadcrumb
    {
        get
        {
            if (_path.Count == 0) return "请选择目标文件夹";
            if (_path.Count <= 3) return string.Join(" / ", _path.Select(item => item.DisplayName));
            return $"{_path[0].DisplayName} / … / {_path[^2].DisplayName} / {_path[^1].DisplayName}";
        }
    }

    public RadialRingView GetRingView(int ringIndex)
    {
        ValidateRingIndex(ringIndex);
        var source = GetRingSource(ringIndex);
        var selected = GetSelectedNode(ringIndex)?.RelativePath;
        if (source.Count == 0)
            return new RadialRingView([null, null, null, null], 0, 0, selected);

        var offset = PositiveModulo(_offsets[ringIndex], source.Count);
        var slots = new FolderMenuNode?[RadialLayout.SlotsPerRing];
        var visibleCount = Math.Min(source.Count, RadialLayout.SlotsPerRing);
        for (var slot = 0; slot < visibleCount; slot++)
            slots[slot] = source[(offset + slot) % source.Count];

        return new RadialRingView(slots, source.Count, offset, selected);
    }

    public FolderMenuNode? GetFolder(int ringIndex, int slotIndex)
    {
        if (slotIndex is < 0 or >= RadialLayout.SlotsPerRing) return null;
        return GetRingView(ringIndex).Slots[slotIndex];
    }

    public bool Hover(int ringIndex, int slotIndex)
    {
        var folder = GetFolder(ringIndex, slotIndex);
        if (folder is null) return false;
        _ = Prefetch(folder);
        var visiblePathCount = VisiblePathCount;

        switch (ringIndex)
        {
            case 0:
                if (visiblePathCount == 1 &&
                    PathsEqual(GetVisiblePathNode(0), folder))
                    return false;
                _outerPreviewPending = false;
                TruncatePath(_anchorDepth);
                _path.Add(folder);
                _outerSelectionHeld = false;
                _offsets[1] = 0;
                _offsets[2] = 0;
                return true;

            case 1:
                if (visiblePathCount == 0) return false;
                _outerPreviewPending = false;
                if (_outerSelectionHeld && visiblePathCount >= 3)
                {
                    _path.RemoveAt(_path.Count - 1);
                    _path[^1] = folder;
                    _outerSelectionHeld = false;
                }
                else if (visiblePathCount == 1)
                {
                    _path.Add(folder);
                }
                else
                {
                    if (PathsEqual(_path[^1], folder)) return false;
                    _path[^1] = folder;
                }

                _offsets[2] = 0;
                return true;

            case 2:
                if (visiblePathCount < 2) return false;
                if (_outerSelectionHeld)
                {
                    if (PathsEqual(_path[^1], folder))
                    {
                        if (!_outerPreviewPending) return false;
                        _outerPreviewPending = false;
                        _outerSelectionHeld = ShouldHoldOuter(folder);
                        if (!_outerSelectionHeld)
                        {
                            _offsets[1] = EnsureVisibleOffset(GetRingSource(1), folder);
                            _offsets[2] = 0;
                        }

                        return true;
                    }

                    _path[^1] = folder;
                }
                else
                {
                    _path.Add(folder);
                }

                _outerPreviewPending = false;
                _outerSelectionHeld = ShouldHoldOuter(folder);
                if (!_outerSelectionHeld)
                {
                    _offsets[1] = EnsureVisibleOffset(GetRingSource(1), folder);
                    _offsets[2] = 0;
                }
                return true;

            default:
                throw new ArgumentOutOfRangeException(nameof(ringIndex));
        }
    }

    public bool Rotate(int ringIndex, int direction)
    {
        ValidateRingIndex(ringIndex);
        var source = GetRingSource(ringIndex);
        if (source.Count <= RadialLayout.SlotsPerRing) return false;
        _offsets[ringIndex] = PositiveModulo(_offsets[ringIndex] + Math.Sign(direction), source.Count);
        return true;
    }

    public bool RotateAndPreview(int ringIndex, int direction, int slotIndex)
    {
        if (!Rotate(ringIndex, direction)) return false;
        // Reading a new outer-ring name must never change the navigation path
        // or center destination, including when an async prefetch completes.
        if (ringIndex == 2)
        {
            var folder = GetFolder(ringIndex, slotIndex);
            if (folder is not null) _ = Prefetch(folder);
        }
        else _ = Hover(ringIndex, slotIndex);
        return true;
    }

    public void Dispose()
    {
        _disposed = true;
        foreach (var list in _observedLists.Keys) list.Changed -= OnChildrenChanged;
        _observedLists.Clear();
    }

    public bool RotateAndHover(int ringIndex, int direction, int slotIndex)
    {
        if (!Rotate(ringIndex, direction)) return false;
        _ = Hover(ringIndex, slotIndex);
        return true;
    }

    public bool NavigateBack()
    {
        if (VisiblePathCount == 0)
        {
            if (_anchorDepth == 0) return false;

            var formerAnchor = _path[^1];
            _path.RemoveAt(_path.Count - 1);
            _anchorDepth--;
            _outerSelectionHeld = false;
            _outerPreviewPending = false;
            _offsets[0] = EnsureVisibleOffset(GetVisibleRootSource(), formerAnchor);
            _offsets[1] = 0;
            _offsets[2] = 0;
            return true;
        }

        var removed = _path[^1];
        _path.RemoveAt(_path.Count - 1);
        _outerSelectionHeld = false;
        _outerPreviewPending = false;

        switch (VisiblePathCount)
        {
            case 0:
                _offsets[0] = EnsureVisibleOffset(GetVisibleRootSource(), removed);
                _offsets[1] = 0;
                _offsets[2] = 0;
                break;

            case 1:
                _offsets[1] = EnsureVisibleOffset(
                    GetVisiblePathNode(0).Children,
                    removed);
                _offsets[2] = 0;
                break;

            default:
                _offsets[1] = EnsureVisibleOffset(GetRingSource(1), _path[^1]);
                _offsets[2] = EnsureVisibleOffset(_path[^1].Children, removed);
                break;
        }

        return true;
    }

    public bool SelectPath(string relativePath)
    {
        var chain = new List<FolderMenuNode>();
        if (!TryFindPath(_rootItems, relativePath, chain)) return false;

        ApplySelectedChain(chain, 0);
        return true;
    }

    public Task<bool> SelectPathAsync(string relativePath) =>
        SelectPathCoreAsync(relativePath, false, null);

    public Task<bool> SelectPathAsync(
        string relativePath,
        string? visibleAnchorPath) =>
        SelectPathCoreAsync(relativePath, false, visibleAnchorPath);

    public Task<bool> SelectStartPathAsync(string relativePath) =>
        SelectPathCoreAsync(relativePath, true, null);

    private async Task<bool> SelectPathCoreAsync(
        string relativePath,
        bool useAsVisibleAnchor,
        string? visibleAnchorPath)
    {
        var target = CompanyArchive.NormalizeRelativePath(relativePath);
        var normalizedAnchor = string.IsNullOrWhiteSpace(visibleAnchorPath)
            ? null
            : CompanyArchive.NormalizeRelativePath(visibleAnchorPath);
        var chain = new List<FolderMenuNode>();
        IReadOnlyList<FolderMenuNode> source = _rootItems;

        while (source.Count > 0)
        {
            var next = source.FirstOrDefault(node =>
                IsSameOrAncestorPath(node.RelativePath, target));
            if (next is null) return false;

            chain.Add(next);
            if (string.Equals(
                    next.RelativePath,
                    target,
                    StringComparison.OrdinalIgnoreCase))
            {
                if (useAsVisibleAnchor &&
                    next.Children is AsyncFolderMenuNodeList anchorChildren)
                {
                    Observe(anchorChildren, next);
                    await anchorChildren.EnsureLoadedAsync();
                }

                var anchorDepth = useAsVisibleAnchor
                    ? chain.Count
                    : GetAnchorDepth(chain, normalizedAnchor);
                ApplySelectedChain(chain, anchorDepth);
                return true;
            }

            if (next.Children is AsyncFolderMenuNodeList asyncChildren)
            {
                Observe(asyncChildren, next);
                await asyncChildren.EnsureLoadedAsync();
            }

            source = next.Children;
        }

        return false;
    }

    public bool Prefetch(FolderMenuNode folder)
    {
        if (_disposed) return false;
        if (folder.Children is not AsyncFolderMenuNodeList asyncChildren) return false;
        Observe(asyncChildren, folder);
        return asyncChildren.BeginLoad();
    }

    public void PrimeForDemo()
    {
        _path.Clear();
        _anchorDepth = 0;
        _outerSelectionHeld = false;
        _outerPreviewPending = false;
        var top = _rootItems.FirstOrDefault(item => item.Children.Count > 0)
                  ?? _rootItems.FirstOrDefault();
        if (top is null) return;
        _path.Add(top);
        _ = Prefetch(top);

        var second = top.Children.FirstOrDefault(item => item.Children.Count > 0)
                     ?? top.Children.FirstOrDefault();
        if (second is not null) _path.Add(second);
    }

    private IReadOnlyList<FolderMenuNode> GetRingSource(int ringIndex)
    {
        var visiblePathCount = VisiblePathCount;
        return ringIndex switch
        {
            0 => GetVisibleRootSource(),
            1 when visiblePathCount == 0 => [],
            1 when visiblePathCount == 1 => GetVisiblePathNode(0).Children,
            1 when _outerSelectionHeld && visiblePathCount >= 3 =>
                GetVisiblePathNode(visiblePathCount - 3).Children,
            1 => GetVisiblePathNode(visiblePathCount - 2).Children,
            2 when _outerSelectionHeld && visiblePathCount >= 3 =>
                GetVisiblePathNode(visiblePathCount - 2).Children,
            2 when visiblePathCount < 2 => [],
            2 => GetVisiblePathNode(visiblePathCount - 1).Children,
            _ => throw new ArgumentOutOfRangeException(nameof(ringIndex))
        };
    }

    private void ApplySelectedChain(
        IReadOnlyList<FolderMenuNode> chain,
        int anchorDepth)
    {
        _path.Clear();
        _path.AddRange(chain);
        _anchorDepth = Math.Clamp(anchorDepth, 0, _path.Count);
        _outerPreviewPending = false;
        Array.Clear(_offsets);
        var visiblePathCount = VisiblePathCount;
        _outerSelectionHeld = visiblePathCount > 0 &&
                              ShouldHoldOuter(_path[^1]);
        if (visiblePathCount > 0)
            _offsets[0] = EnsureVisibleOffset(
                GetVisibleRootSource(),
                GetVisiblePathNode(0));

        if (_outerSelectionHeld && visiblePathCount >= 3)
        {
            _offsets[1] = EnsureVisibleOffset(_path[^3].Children, _path[^2]);
            _offsets[2] = EnsureVisibleOffset(_path[^2].Children, _path[^1]);
        }
        else if (visiblePathCount >= 2)
        {
            _offsets[1] = EnsureVisibleOffset(_path[^2].Children, _path[^1]);
        }
    }

    private void Observe(AsyncFolderMenuNodeList list, FolderMenuNode owner)
    {
        if (_disposed) return;
        if (_observedLists.TryAdd(list, owner))
            list.Changed += OnChildrenChanged;
    }

    private void OnChildrenChanged(object? sender, EventArgs e)
    {
        if (_synchronizationContext is not null &&
            !ReferenceEquals(SynchronizationContext.Current, _synchronizationContext))
        {
            _synchronizationContext.Post(
                _ => ApplyChildrenChanged(sender),
                null);
            return;
        }

        ApplyChildrenChanged(sender);
    }

    private void ApplyChildrenChanged(object? sender)
    {
        if (_disposed) return;
        if (sender is AsyncFolderMenuNodeList list &&
            _observedLists.TryGetValue(list, out var owner) &&
            CurrentDestination is not null &&
            PathsEqual(CurrentDestination, owner) &&
            VisiblePathCount >= 3 &&
            !_outerPreviewPending)
        {
            _outerSelectionHeld = list.Count == 0;
            if (_outerSelectionHeld)
            {
                _offsets[1] = EnsureVisibleOffset(_path[^3].Children, _path[^2]);
                _offsets[2] = EnsureVisibleOffset(_path[^2].Children, _path[^1]);
            }
            else
            {
                _offsets[1] = EnsureVisibleOffset(_path[^2].Children, _path[^1]);
                _offsets[2] = 0;
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private bool PreviewOuterSelection(int slotIndex)
    {
        var folder = GetFolder(2, slotIndex);
        if (folder is null || VisiblePathCount < 2) return false;
        _ = Prefetch(folder);

        if (_outerSelectionHeld)
            _path[^1] = folder;
        else
            _path.Add(folder);

        _outerSelectionHeld = true;
        _outerPreviewPending = true;
        return true;
    }

    private static bool ShouldHoldOuter(FolderMenuNode folder) =>
        folder.Children is AsyncFolderMenuNodeList asyncChildren
            ? !asyncChildren.IsLoaded || asyncChildren.Count == 0
            : folder.Children.Count == 0;

    private static bool IsSameOrAncestorPath(string candidate, string target) =>
        string.Equals(candidate, target, StringComparison.OrdinalIgnoreCase) ||
        target.StartsWith(
            candidate + System.IO.Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);

    private FolderMenuNode? GetSelectedNode(int ringIndex)
    {
        var visiblePathCount = VisiblePathCount;
        return ringIndex switch
        {
            0 when visiblePathCount >= 1 => GetVisiblePathNode(0),
            1 when _outerSelectionHeld && visiblePathCount >= 3 => _path[^2],
            1 when visiblePathCount >= 2 => _path[^1],
            2 when _outerSelectionHeld && visiblePathCount >= 3 => _path[^1],
            _ => null
        };
    }

    private int VisiblePathCount => _path.Count - _anchorDepth;

    private IReadOnlyList<FolderMenuNode> GetVisibleRootSource() =>
        _anchorDepth == 0
            ? _rootItems
            : _path[_anchorDepth - 1].Children;

    private FolderMenuNode GetVisiblePathNode(int index) =>
        _path[_anchorDepth + index];

    private void TruncatePath(int count)
    {
        while (_path.Count > count)
            _path.RemoveAt(_path.Count - 1);
    }

    private static int GetAnchorDepth(
        IReadOnlyList<FolderMenuNode> chain,
        string? anchorPath)
    {
        if (string.IsNullOrWhiteSpace(anchorPath)) return 0;
        for (var index = 0; index < chain.Count; index++)
        {
            if (string.Equals(
                    chain[index].RelativePath,
                    anchorPath,
                    StringComparison.OrdinalIgnoreCase))
                return index + 1;
        }

        return 0;
    }

    private static int EnsureVisibleOffset(
        IReadOnlyList<FolderMenuNode> source,
        FolderMenuNode selected)
    {
        for (var index = 0; index < source.Count; index++)
        {
            if (PathsEqual(source[index], selected)) return index;
        }

        return 0;
    }

    private static bool TryFindPath(
        IReadOnlyList<FolderMenuNode> nodes,
        string relativePath,
        List<FolderMenuNode> chain)
    {
        foreach (var node in nodes)
        {
            chain.Add(node);
            if (string.Equals(
                    node.RelativePath,
                    relativePath,
                    StringComparison.OrdinalIgnoreCase))
                return true;
            if (TryFindPath(node.Children, relativePath, chain)) return true;
            chain.RemoveAt(chain.Count - 1);
        }

        return false;
    }

    private static bool PathsEqual(FolderMenuNode left, FolderMenuNode right) =>
        string.Equals(left.RelativePath, right.RelativePath, StringComparison.OrdinalIgnoreCase);

    private static int PositiveModulo(int value, int divisor)
    {
        if (divisor <= 0) return 0;
        var result = value % divisor;
        return result < 0 ? result + divisor : result;
    }

    private static void ValidateRingIndex(int ringIndex)
    {
        if (ringIndex is < 0 or > 2)
            throw new ArgumentOutOfRangeException(nameof(ringIndex));
    }
}
