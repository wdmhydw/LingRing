namespace GongSiOrganizer.Core;

public sealed record DirectMoveResult(
    string SourcePath,
    string DestinationPath,
    bool WasDirectory);

public sealed class DirectMoveService
{
    private readonly bool _forcePerFileDirectoryMove;
    private readonly bool _forceCopyDeleteFileMove;

    public DirectMoveService(
        bool forcePerFileDirectoryMove = false,
        bool forceCopyDeleteFileMove = false)
    {
        _forcePerFileDirectoryMove = forcePerFileDirectoryMove;
        _forceCopyDeleteFileMove = forceCopyDeleteFileMove;
    }

    public DirectMoveResult MoveToArchive(
        string sourcePath,
        string archiveRoot,
        string relativeDestination)
    {
        var destination = ArchiveScope.ResolveDestination(
            archiveRoot,
            relativeDestination);
        return Move(sourcePath, destination);
    }

    public DirectMoveResult Move(string sourcePath, string destinationDirectory)
    {
        var source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourcePath));
        var destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destinationDirectory));
        var isFile = File.Exists(source);
        var isDirectory = Directory.Exists(source);

        if (!isFile && !isDirectory)
            throw new FileNotFoundException("所选文件或文件夹已经不存在。", source);
        if (!Directory.Exists(destination))
            throw new DirectoryNotFoundException($"目标目录不存在：{destination}");

        var sourceName = Path.GetFileName(source);
        if (string.IsNullOrWhiteSpace(sourceName))
            throw new InvalidOperationException("不能移动磁盘根目录。");

        var proposedPath = Path.Combine(destination, sourceName);
        if (PathsEqual(source, proposedPath))
            throw new InvalidOperationException($"“{sourceName}”已经在这个目录中。");
        if (isDirectory && CompanyArchive.IsWithinRoot(destination, source))
            throw new InvalidOperationException("不能把文件夹移动到它自己的内部。");

        var finalPath = FindAvailablePath(proposedPath, isDirectory);
        if (isFile)
        {
            if (_forceCopyDeleteFileMove || !SameVolume(source, finalPath))
                MoveFileAcrossVolumes(source, finalPath);
            else
                File.Move(source, finalPath, false);
        }
        else if (!_forcePerFileDirectoryMove && SameVolume(source, finalPath))
        {
            Directory.Move(source, finalPath);
        }
        else
        {
            MoveDirectoryAcrossVolumes(source, finalPath);
        }

        return new DirectMoveResult(source, finalPath, isDirectory);
    }

    private static void MoveDirectoryAcrossVolumes(string sourceRoot, string destinationRoot)
    {
        var directories = new List<string>();
        var files = new List<string>();
        var pending = new Stack<string>();
        pending.Push(sourceRoot);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            EnsureNotSymbolicLink(new DirectoryInfo(current));
            directories.Add(current);

            foreach (var childDirectory in Directory.EnumerateDirectories(current))
                pending.Push(childDirectory);
            foreach (var file in Directory.EnumerateFiles(current))
            {
                EnsureNotSymbolicLink(new FileInfo(file));
                files.Add(file);
            }
        }

        var createdDirectories = new List<string>(directories.Count);
        var movedFileCount = 0;
        try
        {
            foreach (var directory in directories.OrderBy(path => path.Length))
            {
                var relative = Path.GetRelativePath(sourceRoot, directory);
                var target = relative == "." ? destinationRoot : Path.Combine(destinationRoot, relative);
                Directory.CreateDirectory(target);
                createdDirectories.Add(target);
            }

            foreach (var file in files)
            {
                var target = Path.Combine(destinationRoot, Path.GetRelativePath(sourceRoot, file));
                MoveFileAcrossVolumes(file, target);
                movedFileCount++;
            }

            foreach (var directory in directories.OrderByDescending(path => path.Length))
                Directory.Delete(directory, false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (movedFileCount == 0)
                RemoveEmptyDirectories(createdDirectories);

            var detail = movedFileCount == 0
                ? "没有文件被移动。"
                : $"已有 {movedFileCount} 个文件直接移动到目标，剩余内容仍在原文件夹。";
            throw new IOException($"跨盘移动文件夹未能完整完成。{detail}", exception);
        }
    }

    private static void MoveFileAcrossVolumes(string source, string destination)
    {
        try
        {
            File.Copy(source, destination, false);
        }
        catch
        {
            try
            {
                if (File.Exists(destination)) File.Delete(destination);
            }
            catch
            {
            }

            throw;
        }

        try
        {
            File.Delete(source);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            try
            {
                File.Delete(destination);
            }
            catch
            {
                // The source still exists. Report the original deletion error
                // even if cleanup of the destination also failed.
            }

            throw new IOException("目标文件已写入，但无法删除源文件；移动已回滚。", exception);
        }
    }

    private static void EnsureNotSymbolicLink(FileSystemInfo item)
    {
        if (!string.IsNullOrWhiteSpace(item.LinkTarget))
            throw new IOException($"暂不移动符号链接或联接点：{item.FullName}");
    }

    private static void RemoveEmptyDirectories(IEnumerable<string> directories)
    {
        foreach (var directory in directories.OrderByDescending(path => path.Length))
        {
            try
            {
                Directory.Delete(directory, false);
            }
            catch
            {
                // 只清理本次创建且仍为空的目录；任何非空内容都原样保留。
            }
        }
    }

    private static string FindAvailablePath(string proposedPath, bool isDirectory)
    {
        if (!File.Exists(proposedPath) && !Directory.Exists(proposedPath)) return proposedPath;

        var parent = Path.GetDirectoryName(proposedPath)
                     ?? throw new InvalidOperationException("目标目录无效。");
        var originalName = Path.GetFileName(proposedPath);
        var extension = isDirectory ? string.Empty : Path.GetExtension(originalName);
        var stem = isDirectory ? originalName : Path.GetFileNameWithoutExtension(originalName);

        for (var index = 1; index < 100_000; index++)
        {
            var candidate = Path.Combine(parent, $"{stem} ({index}){extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        }

        throw new IOException("无法生成不冲突的目标名称。");
    }

    private static bool SameVolume(string left, string right) =>
        string.Equals(
            Path.GetPathRoot(Path.GetFullPath(left)),
            Path.GetPathRoot(Path.GetFullPath(right)),
            StringComparison.OrdinalIgnoreCase);

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            StringComparison.OrdinalIgnoreCase);
}
