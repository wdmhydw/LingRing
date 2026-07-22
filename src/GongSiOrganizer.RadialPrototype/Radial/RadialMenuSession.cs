using GongSiOrganizer.Core;

namespace GongSiOrganizer.RadialPrototype.Radial;

public sealed record RadialRingView(
    IReadOnlyList<FolderMenuNode?> Slots,
    int TotalCount,
    int Offset,
    string? SelectedRelativePath);

public sealed class RadialMenuSession
{
    private readonly IReadOnlyList<FolderMenuNode> _rootItems;
    private readonly List<FolderMenuNode> _path = [];
    private readonly int[] _offsets = [0, 0, 0];
    private bool _outerSelectionHeld;

    public RadialMenuSession(IReadOnlyList<FolderMenuNode> rootItems)
    {
        _rootItems = rootItems;
    }

    public IReadOnlyList<FolderMenuNode> Path => _path;
    public FolderMenuNode? CurrentDestination => _path.LastOrDefault();

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

        switch (ringIndex)
        {
            case 0:
                if (_path.Count == 1 && PathsEqual(_path[0], folder)) return false;
                _path.Clear();
                _path.Add(folder);
                _outerSelectionHeld = false;
                _offsets[1] = 0;
                _offsets[2] = 0;
                return true;

            case 1:
                if (_path.Count == 0) return false;
                if (_outerSelectionHeld && _path.Count >= 3)
                {
                    _path.RemoveAt(_path.Count - 1);
                    _path[^1] = folder;
                    _outerSelectionHeld = false;
                }
                else if (_path.Count == 1)
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
                if (_path.Count < 2) return false;
                if (_outerSelectionHeld)
                {
                    if (PathsEqual(_path[^1], folder)) return false;
                    _path[^1] = folder;
                }
                else
                {
                    _path.Add(folder);
                }

                _outerSelectionHeld = folder.Children.Count == 0;
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

    public bool RotateAndHover(int ringIndex, int direction, int slotIndex)
    {
        if (!Rotate(ringIndex, direction)) return false;
        _ = Hover(ringIndex, slotIndex);
        return true;
    }

    public bool NavigateBack()
    {
        if (_path.Count == 0) return false;

        var removed = _path[^1];
        _path.RemoveAt(_path.Count - 1);
        _outerSelectionHeld = false;

        switch (_path.Count)
        {
            case 0:
                _offsets[0] = EnsureVisibleOffset(_rootItems, removed);
                _offsets[1] = 0;
                _offsets[2] = 0;
                break;

            case 1:
                _offsets[1] = EnsureVisibleOffset(_path[0].Children, removed);
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

        _path.Clear();
        _path.AddRange(chain);
        Array.Clear(_offsets);
        _outerSelectionHeld = _path[^1].Children.Count == 0;
        _offsets[0] = EnsureVisibleOffset(_rootItems, _path[0]);

        if (_outerSelectionHeld && _path.Count >= 3)
        {
            _offsets[1] = EnsureVisibleOffset(_path[^3].Children, _path[^2]);
            _offsets[2] = EnsureVisibleOffset(_path[^2].Children, _path[^1]);
        }
        else if (_path.Count >= 2)
        {
            _offsets[1] = EnsureVisibleOffset(_path[^2].Children, _path[^1]);
        }

        return true;
    }

    public void PrimeForDemo()
    {
        _path.Clear();
        _outerSelectionHeld = false;
        var top = _rootItems.FirstOrDefault(item => item.Children.Count > 0)
                  ?? _rootItems.FirstOrDefault();
        if (top is null) return;
        _path.Add(top);

        var second = top.Children.FirstOrDefault(item => item.Children.Count > 0)
                     ?? top.Children.FirstOrDefault();
        if (second is not null) _path.Add(second);
    }

    private IReadOnlyList<FolderMenuNode> GetRingSource(int ringIndex) => ringIndex switch
    {
        0 => _rootItems,
        1 when _path.Count == 0 => [],
        1 when _path.Count == 1 => _path[0].Children,
        1 when _outerSelectionHeld && _path.Count >= 3 => _path[^3].Children,
        1 => _path[^2].Children,
        2 when _outerSelectionHeld && _path.Count >= 3 => _path[^2].Children,
        2 when _path.Count < 2 => [],
        2 => _path[^1].Children,
        _ => throw new ArgumentOutOfRangeException(nameof(ringIndex))
    };

    private FolderMenuNode? GetSelectedNode(int ringIndex) => ringIndex switch
    {
        0 => _path.FirstOrDefault(),
        1 when _outerSelectionHeld && _path.Count >= 3 => _path[^2],
        1 when _path.Count >= 2 => _path[^1],
        2 when _outerSelectionHeld && _path.Count >= 3 => _path[^1],
        _ => null
    };

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
