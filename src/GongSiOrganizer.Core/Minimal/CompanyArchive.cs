using System.Text;

namespace GongSiOrganizer.Core;

public static class CompanyArchive
{
    public const string DefaultRoot = @"E:\GongSi";

    public static string NormalizeRoot(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            throw new ArgumentException("公司目录不能为空。", nameof(rootPath));

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath.Trim()));
    }

    public static string NormalizeRelativePath(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || relativePath == ".") return ".";

        var normalized = relativePath
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .Trim(Path.DirectorySeparatorChar, ' ');

        if (Path.IsPathRooted(normalized))
            throw new ArgumentException("目标必须是公司目录内的相对路径。", nameof(relativePath));

        var segments = normalized.Split(
            Path.DirectorySeparatorChar,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (segments.Length == 0) return ".";
        if (segments.Any(segment => segment is "." or ".." || segment.Contains('\0')))
            throw new ArgumentException("目标路径包含不允许的路径片段。", nameof(relativePath));

        return Path.Combine(segments);
    }

    public static string ResolveDestination(string rootPath, string relativePath)
    {
        var root = NormalizeRoot(rootPath);
        var relative = NormalizeRelativePath(relativePath);
        var destination = relative == "."
            ? root
            : Path.GetFullPath(Path.Combine(root, relative));

        if (!IsWithinRoot(destination, root))
            throw new InvalidOperationException("目标目录越出了 E:\\GongSi，操作已阻止。");

        return destination;
    }

    public static bool IsWithinRoot(string candidatePath, string rootPath)
    {
        var candidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidatePath));
        var root = NormalizeRoot(rootPath);
        var rootPrefix = Path.EndsInDirectorySeparator(root)
            ? root
            : root + Path.DirectorySeparatorChar;
        return candidate.Equals(root, StringComparison.OrdinalIgnoreCase) ||
               candidate.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase);
    }
}

public static class DestinationToken
{
    public static string Encode(string relativePath)
    {
        var normalized = CompanyArchive.NormalizeRelativePath(relativePath);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(normalized))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static string Decode(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new ArgumentException("目标目录标识为空。", nameof(token));

        var base64 = token.Replace('-', '+').Replace('_', '/');
        base64 += (base64.Length % 4) switch
        {
            0 => "",
            2 => "==",
            3 => "=",
            _ => throw new FormatException("目标目录标识无效。")
        };

        var relative = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
        return CompanyArchive.NormalizeRelativePath(relative);
    }
}
