namespace GongSiOrganizer.Core;

public sealed record FolderMenuNode(
    string DisplayName,
    string RelativePath,
    IReadOnlyList<FolderMenuNode> Children);

public sealed record FolderCatalogResult(
    string RootPath,
    IReadOnlyList<FolderMenuNode> Folders,
    int FolderCount,
    int SkippedCount);

public sealed class FolderCatalog
{
    private const int MaximumDepth = 32;
    private const int MaximumFolders = 10_000;

    private static readonly HashSet<string> ExcludedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".SynologyWorkingDirectory",
        "$RECYCLE.BIN",
        "System Volume Information"
    };

    private int _folderCount;
    private int _skippedCount;

    public FolderCatalogResult Scan(string rootPath)
    {
        var root = CompanyArchive.NormalizeRoot(rootPath);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"公司目录不存在：{root}");

        _folderCount = 0;
        _skippedCount = 0;
        var folders = ReadChildren(root, root, 0);
        return new FolderCatalogResult(root, folders, _folderCount, _skippedCount);
    }

    private IReadOnlyList<FolderMenuNode> ReadChildren(string root, string parentPath, int depth)
    {
        if (depth >= MaximumDepth)
        {
            _skippedCount++;
            return [];
        }

        DirectoryInfo[] directories;
        try
        {
            directories = new DirectoryInfo(parentPath).EnumerateDirectories().ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _skippedCount++;
            return [];
        }

        var result = new List<FolderMenuNode>(directories.Length);
        foreach (var directory in directories.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            if (!ShouldInclude(directory))
            {
                _skippedCount++;
                continue;
            }

            _folderCount++;
            if (_folderCount > MaximumFolders)
                throw new InvalidOperationException($"目录数量超过 {MaximumFolders:N0} 个，无法生成可用的右键菜单。");

            var relative = Path.GetRelativePath(root, directory.FullName);
            var children = ReadChildren(root, directory.FullName, depth + 1);
            result.Add(new FolderMenuNode(directory.Name, relative, children));
        }

        return result;
    }

    private static bool ShouldInclude(DirectoryInfo directory)
    {
        if (ExcludedNames.Contains(directory.Name)) return false;

        try
        {
            if ((directory.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) return false;

            // Synology/OneDrive 占位目录也带 ReparsePoint，但 LinkTarget 为空；它们需要正常显示。
            // 只有真正指向别处的符号链接或联接点才跳过，以免目录树循环。
            if (!string.IsNullOrWhiteSpace(directory.LinkTarget)) return false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        return true;
    }
}
