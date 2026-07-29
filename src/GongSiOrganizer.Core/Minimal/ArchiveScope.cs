namespace GongSiOrganizer.Core;

/// <summary>
/// Describes the boundary in which archive destinations may be selected.
/// A normal scope is a single directory. <see cref="ThisPcRoot"/> is a
/// virtual scope whose first path segment is a ready local drive letter.
/// </summary>
public static class ArchiveScope
{
    public const string ThisPcRoot = "shell:ThisPC";

    private static readonly DriveType[] SupportedDriveTypes =
    [
        DriveType.Fixed,
        DriveType.Removable
    ];

    public static bool IsThisPc(string? rootPath) =>
        string.Equals(rootPath?.Trim(), ThisPcRoot, StringComparison.OrdinalIgnoreCase);

    public static string Normalize(string rootPath) =>
        IsThisPc(rootPath)
            ? ThisPcRoot
            : CompanyArchive.NormalizeRoot(rootPath);

    public static bool IsAvailable(string? rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath)) return false;
        if (!IsThisPc(rootPath)) return Directory.Exists(rootPath);
        return GetReadyLocalDrives().Count > 0;
    }

    public static string GetDisplayName(string rootPath) =>
        IsThisPc(rootPath)
            ? "此电脑（所有本地磁盘）"
            : CompanyArchive.NormalizeRoot(rootPath);

    public static ArchiveNavigationProfile CreateNavigationProfile(string selectedPath)
    {
        if (IsThisPc(selectedPath)) return ArchiveNavigationProfile.WholeComputer;

        var normalized = CompanyArchive.NormalizeRoot(selectedPath);
        var driveRoot = Path.GetPathRoot(normalized);
        if (!string.IsNullOrWhiteSpace(driveRoot) &&
            driveRoot.Length >= 2 &&
            char.IsAsciiLetter(driveRoot[0]) &&
            driveRoot[1] == Path.VolumeSeparatorChar)
        {
            return new ArchiveNavigationProfile(
                ThisPcRoot,
                CreateChildToken(normalized, driveRoot));
        }

        // Keep UNC/legacy locations usable even though they are not descendants
        // of the local-drive-only "This PC" scope.
        return new ArchiveNavigationProfile(normalized, null);
    }

    public static ArchiveNavigationProfile NormalizeNavigationProfile(
        string rootPath,
        string? startPath)
    {
        var normalizedRoot = Normalize(rootPath);
        if (!IsThisPc(normalizedRoot))
            return CreateNavigationProfile(normalizedRoot);

        return new ArchiveNavigationProfile(
            ThisPcRoot,
            string.IsNullOrWhiteSpace(startPath)
                ? null
                : NormalizeDestinationToken(ThisPcRoot, startPath));
    }

    public static string GetNavigationSelectionPath(ArchiveNavigationProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var normalized = NormalizeNavigationProfile(profile.RootPath, profile.StartPath);
        if (string.IsNullOrWhiteSpace(normalized.StartPath))
            return normalized.RootPath;
        if (!IsThisPc(normalized.RootPath))
            return ResolveDestination(normalized.RootPath, normalized.StartPath);

        var segments = SplitToken(normalized.StartPath);
        var driveRoot = $"{segments[0]}:{Path.DirectorySeparatorChar}";
        return segments.Length == 1
            ? driveRoot
            : Path.Combine(driveRoot, Path.Combine(segments[1..]));
    }

    public static string GetNavigationDisplayName(ArchiveNavigationProfile profile) =>
        GetDisplayName(GetNavigationSelectionPath(profile));

    public static bool IsNavigationAvailable(ArchiveNavigationProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        try
        {
            var normalized = NormalizeNavigationProfile(profile.RootPath, profile.StartPath);
            if (!IsAvailable(normalized.RootPath)) return false;
            return string.IsNullOrWhiteSpace(normalized.StartPath) ||
                   Directory.Exists(ResolveDestination(
                       normalized.RootPath,
                       normalized.StartPath));
        }
        catch (Exception exception) when (
            exception is ArgumentException or
                InvalidOperationException or
                IOException or
                NotSupportedException or
                UnauthorizedAccessException or
                System.Security.SecurityException)
        {
            return false;
        }
    }

    public static IReadOnlyList<DriveInfo> GetReadyLocalDrives()
    {
        var drives = new List<DriveInfo>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady || !SupportedDriveTypes.Contains(drive.DriveType)) continue;
                drives.Add(drive);
            }
            catch (Exception exception) when (
                exception is IOException or
                    UnauthorizedAccessException or
                    System.Security.SecurityException)
            {
                // A drive can disappear between discovery and inspection.
            }
        }

        return drives
            .OrderBy(drive => drive.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static string ResolveDestination(string rootPath, string destinationToken)
    {
        var root = Normalize(rootPath);
        return IsThisPc(root)
            ? ResolveThisPcDestination(destinationToken, GetReadyLocalDrives().Select(drive => drive.Name))
            : CompanyArchive.ResolveDestination(root, destinationToken);
    }

    public static string NormalizeDestinationToken(string rootPath, string destinationToken)
    {
        var root = Normalize(rootPath);
        if (!IsThisPc(root))
            return CompanyArchive.NormalizeRelativePath(destinationToken);

        var normalized = CompanyArchive.NormalizeRelativePath(destinationToken);
        var segments = SplitToken(normalized);
        if (segments.Length == 0 || !IsDriveLetterSegment(segments[0]))
            throw new ArgumentException(
                "“此电脑”目标必须先选择一个本地磁盘。",
                nameof(destinationToken));

        segments[0] = segments[0].ToUpperInvariant();
        return Path.Combine(segments);
    }

    public static string CombineDestinationToken(
        string rootPath,
        string parentToken,
        string childName)
    {
        var parent = NormalizeDestinationToken(rootPath, parentToken);
        if (string.IsNullOrWhiteSpace(childName))
            throw new ArgumentException("子目录名称不能为空。", nameof(childName));
        return NormalizeDestinationToken(rootPath, Path.Combine(parent, childName));
    }

    public static string GetDestinationToken(string rootPath, string fullPath)
    {
        var root = Normalize(rootPath);
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(fullPath));
        if (!IsThisPc(root))
        {
            if (!CompanyArchive.IsWithinRoot(full, root))
                throw new InvalidOperationException("目标路径超出了当前归档根目录。");
            return CompanyArchive.NormalizeRelativePath(Path.GetRelativePath(root, full));
        }

        var driveRoot = Path.GetPathRoot(full);
        if (string.IsNullOrWhiteSpace(driveRoot))
            throw new InvalidOperationException("无法确定目标所在磁盘。");

        var allowedRoot = GetReadyLocalDrives()
            .Select(drive => Path.TrimEndingDirectorySeparator(drive.Name))
            .FirstOrDefault(candidate =>
                candidate.Equals(
                    Path.TrimEndingDirectorySeparator(driveRoot),
                    StringComparison.OrdinalIgnoreCase));
        if (allowedRoot is null)
            throw new InvalidOperationException("目标不在当前可用的本地磁盘中。");

        var driveLetter = char.ToUpperInvariant(allowedRoot[0]).ToString();
        var relative = Path.GetRelativePath(driveRoot, full);
        return relative == "."
            ? driveLetter
            : Path.Combine(driveLetter, relative);
    }

    public static string GetDestinationDisplayPath(string rootPath, string fullPath)
    {
        if (!IsThisPc(rootPath))
            return Path.GetRelativePath(CompanyArchive.NormalizeRoot(rootPath), fullPath);

        var token = GetDestinationToken(rootPath, fullPath);
        var segments = SplitToken(token);
        var driveRoot = $"{segments[0]}:{Path.DirectorySeparatorChar}";
        return segments.Length == 1
            ? driveRoot
            : Path.Combine(driveRoot, Path.Combine(segments[1..]));
    }

    public static string CreateDriveToken(string driveRoot)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(driveRoot));
        if (string.IsNullOrWhiteSpace(root) || !char.IsAsciiLetter(root[0]))
            throw new ArgumentException("磁盘根路径无效。", nameof(driveRoot));
        return char.ToUpperInvariant(root[0]).ToString();
    }

    public static string CreateChildToken(
        string childFullPath,
        string driveRoot)
    {
        var driveToken = CreateDriveToken(driveRoot);
        var relative = Path.GetRelativePath(driveRoot, childFullPath);
        return relative == "."
            ? driveToken
            : Path.Combine(driveToken, relative);
    }

    internal static string ResolveThisPcDestination(
        string destinationToken,
        IEnumerable<string> allowedDriveRoots)
    {
        var normalized = CompanyArchive.NormalizeRelativePath(destinationToken);
        var segments = SplitToken(normalized);
        if (segments.Length == 0 || !IsDriveLetterSegment(segments[0]))
            throw new ArgumentException(
                "“此电脑”目标必须先选择一个本地磁盘。",
                nameof(destinationToken));

        var requestedDrive = $"{char.ToUpperInvariant(segments[0][0])}:{Path.DirectorySeparatorChar}";
        var allowedDrive = allowedDriveRoots
            .Select(path => Path.GetPathRoot(Path.GetFullPath(path)))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .FirstOrDefault(path =>
                string.Equals(path, requestedDrive, StringComparison.OrdinalIgnoreCase));
        if (allowedDrive is null)
            throw new InvalidOperationException("所选磁盘当前不可用，或不是本地固定盘/可移动盘。");

        var destination = segments.Length == 1
            ? allowedDrive
            : Path.GetFullPath(Path.Combine(allowedDrive, Path.Combine(segments[1..])));
        if (!CompanyArchive.IsWithinRoot(destination, allowedDrive))
            throw new InvalidOperationException("目标路径超出了所选磁盘。");
        return Path.TrimEndingDirectorySeparator(destination);
    }

    private static string[] SplitToken(string token) =>
        token == "."
            ? []
            : token.Split(
                Path.DirectorySeparatorChar,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool IsDriveLetterSegment(string value) =>
        value.Length == 1 && char.IsAsciiLetter(value[0]);
}
