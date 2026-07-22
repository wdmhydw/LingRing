using System.IO;

namespace GongSiOrganizer.RadialPrototype.Services;

internal static class OrganizerDataPath
{
    private static readonly Lazy<string> WritableDirectory = new(ResolveWritableDirectory);

    public static string ForFile(string fileName) =>
        Path.Combine(WritableDirectory.Value, fileName);

    private static string ResolveWritableDirectory()
    {
        var candidates = new[]
        {
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GongSiOrganizer.RadialPrototype"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "GongSiOrganizer.RadialPrototype"),
            Path.Combine(AppContext.BaseDirectory, "data"),
            Path.Combine(Path.GetTempPath(), "GongSiOrganizer.RadialPrototype")
        };

        foreach (var directory in candidates.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            if (CanWriteTo(directory)) return directory;
        }

        return Path.GetTempPath();
    }

    private static bool CanWriteTo(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var probePath = Path.Combine(directory, $".write-probe-{Guid.NewGuid():N}.tmp");
            using var stream = new FileStream(
                probePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                1,
                FileOptions.DeleteOnClose);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                System.Security.SecurityException)
        {
            return false;
        }
    }
}
