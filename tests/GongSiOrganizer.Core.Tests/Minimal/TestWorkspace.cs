namespace GongSiOrganizer.Core.Tests;

internal sealed class TestWorkspace : IDisposable
{
    public TestWorkspace()
    {
        Root = Path.Combine(
            Path.GetTempPath(),
            "GongSiOrganizer.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string DirectoryAt(params string[] parts)
    {
        var path = parts.Aggregate(Root, Path.Combine);
        Directory.CreateDirectory(path);
        return path;
    }

    public string FileAt(string content, params string[] parts)
    {
        var path = parts.Aggregate(Root, Path.Combine);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(Root)) Directory.Delete(Root, true);
    }
}
