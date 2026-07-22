namespace GongSiOrganizer.Core;

public sealed record SubfolderCreationResult(
    string FullPath,
    string RelativePath,
    string DisplayName);

public sealed class SubfolderCreationService
{
    public const int MaximumNameLength = 100;

    private static readonly HashSet<string> ReservedDeviceNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

    public SubfolderCreationResult Create(
        string rootPath,
        string parentRelativePath,
        string requestedName)
    {
        var root = CompanyArchive.NormalizeRoot(rootPath);
        var parent = CompanyArchive.ResolveDestination(root, parentRelativePath);
        if (!Directory.Exists(parent))
            throw new DirectoryNotFoundException("所选父文件夹已不存在，请刷新目录后重试。");

        var name = ValidateName(requestedName);
        var destination = Path.GetFullPath(Path.Combine(parent, name));
        if (!CompanyArchive.IsWithinRoot(destination, root))
            throw new InvalidOperationException("新文件夹路径越出了公司目录，操作已阻止。");
        if (Directory.Exists(destination) || File.Exists(destination))
            throw new IOException($"“{name}”已经存在，请换一个名称。");

        Directory.CreateDirectory(destination);
        return new SubfolderCreationResult(
            destination,
            Path.GetRelativePath(root, destination),
            name);
    }

    public static string ValidateName(string? requestedName)
    {
        var name = requestedName?.Trim() ?? string.Empty;
        if (name.Length == 0)
            throw new ArgumentException("请输入文件夹名称。", nameof(requestedName));
        if (name.Length > MaximumNameLength)
            throw new ArgumentException(
                $"文件夹名称不能超过 {MaximumNameLength} 个字符。",
                nameof(requestedName));
        if (name is "." or "..")
            throw new ArgumentException("不能使用“.”或“..”作为文件夹名称。", nameof(requestedName));
        if (name.EndsWith('.') || name.EndsWith(' '))
            throw new ArgumentException("文件夹名称不能以空格或句点结尾。", nameof(requestedName));
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            name.Contains(Path.DirectorySeparatorChar) ||
            name.Contains(Path.AltDirectorySeparatorChar))
            throw new ArgumentException("名称中包含 Windows 不允许的字符。", nameof(requestedName));

        var deviceName = name.Split('.')[0];
        if (ReservedDeviceNames.Contains(deviceName))
            throw new ArgumentException("该名称是 Windows 保留名称，请换一个名称。", nameof(requestedName));

        return name;
    }
}
