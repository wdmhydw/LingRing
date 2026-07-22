namespace GongSiOrganizer.Core;

public sealed record DirectMoveFailure(
    string SourcePath,
    string Message);

public sealed record DirectMoveBatchResult(
    IReadOnlyList<DirectMoveResult> Moved,
    IReadOnlyList<DirectMoveFailure> Failures)
{
    public int TotalCount => Moved.Count + Failures.Count;
}

public sealed class DirectMoveBatchService
{
    private readonly DirectMoveService _mover;

    public DirectMoveBatchService(DirectMoveService? mover = null)
    {
        _mover = mover ?? new DirectMoveService();
    }

    public DirectMoveBatchResult MoveToArchive(
        IEnumerable<string> sourcePaths,
        string archiveRoot,
        string relativeDestination)
    {
        var destination = CompanyArchive.ResolveDestination(
            archiveRoot,
            relativeDestination);
        if (!Directory.Exists(destination))
            throw new DirectoryNotFoundException($"目标目录不存在：{destination}");

        var sources = sourcePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var moved = new List<DirectMoveResult>(sources.Length);
        var failures = new List<DirectMoveFailure>();
        foreach (var source in sources)
        {
            try
            {
                moved.Add(_mover.Move(source, destination));
            }
            catch (Exception exception)
            {
                failures.Add(new DirectMoveFailure(source, exception.Message));
            }
        }

        return new DirectMoveBatchResult(moved, failures);
    }
}
