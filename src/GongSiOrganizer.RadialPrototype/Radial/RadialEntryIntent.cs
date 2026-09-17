namespace GongSiOrganizer.RadialPrototype.Radial;

// Navigation intent is independent from preview and file movement. No UI clock,
// filesystem access or platform API is needed to invalidate a pending gesture.
internal sealed class RadialEntryIntent
{
    private string? _bodyPath;
    private string? _arcPath;
    private int _generation;

    public bool IsPending => _arcPath is not null;
    public bool IsLocked { get; private set; }

    public void VisitBody(string path, int generation)
    {
        _bodyPath = path;
        _generation = generation;
        _arcPath = null;
        IsLocked = false;
    }

    public bool VisitArc(string path, int generation)
    {
        if (IsLocked || generation != _generation || !Same(_bodyPath, path)) return false;
        if (Same(_arcPath, path)) return false;
        _arcPath = path;
        return true;
    }

    public bool TryEnter(string path, int generation)
    {
        if (!IsPending || IsLocked || generation != _generation || !Same(_arcPath, path))
            return false;
        Lock();
        return true;
    }

    public void Cancel(bool forgetBody = false)
    {
        _arcPath = null;
        if (forgetBody) _bodyPath = null;
    }

    public void Lock()
    {
        Cancel(true);
        IsLocked = true;
    }

    private static bool Same(string? a, string b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
