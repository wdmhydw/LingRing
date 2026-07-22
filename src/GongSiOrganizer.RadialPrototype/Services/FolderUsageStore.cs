using System.IO;
using System.Text.Json;
using GongSiOrganizer.Core;

namespace GongSiOrganizer.RadialPrototype.Services;

internal sealed class FolderUsageStore
{
    private const int CurrentVersion = 2;
    private readonly object _gate = new();
    private readonly string _storagePath;
    private readonly Dictionary<string, Dictionary<string, UsageEntry>> _profiles =
        new(StringComparer.OrdinalIgnoreCase);

    internal FolderUsageStore(string storagePath)
    {
        _storagePath = Path.GetFullPath(storagePath);
        Load();
    }

    public static FolderUsageStore CreateDefault() =>
        new(OrganizerDataPath.ForFile("folder-usage.json"));

    public FolderCatalogResult Apply(FolderCatalogResult snapshot)
    {
        Dictionary<string, UsageEntry> entries;
        lock (_gate)
        {
            var root = CompanyArchive.NormalizeRoot(snapshot.RootPath);
            entries = _profiles.TryGetValue(root, out var profile)
                ? new Dictionary<string, UsageEntry>(profile, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, UsageEntry>(StringComparer.OrdinalIgnoreCase);
        }

        return snapshot with { Folders = Rank(snapshot.Folders, entries) };
    }

    public void RecordSelection(string rootPath, string relativeDestination)
    {
        var prefixes = GetPrefixes(relativeDestination);
        if (prefixes.Count == 0) return;
        var root = CompanyArchive.NormalizeRoot(rootPath);

        lock (_gate)
        {
            if (!_profiles.TryGetValue(root, out var entries))
            {
                entries = new Dictionary<string, UsageEntry>(StringComparer.OrdinalIgnoreCase);
                _profiles[root] = entries;
            }

            var nowTicks = DateTime.UtcNow.Ticks;
            foreach (var prefix in prefixes)
            {
                entries.TryGetValue(prefix, out var current);
                var count = current?.Count >= long.MaxValue
                    ? long.MaxValue
                    : (current?.Count ?? 0) + 1;
                entries[prefix] = new UsageEntry(count, nowTicks);
            }

            SaveUnsafe();
        }
    }

    public void Clear(string rootPath)
    {
        var root = CompanyArchive.NormalizeRoot(rootPath);
        lock (_gate)
        {
            _profiles.Remove(root);
            SaveUnsafe();
        }
    }

    private static IReadOnlyList<FolderMenuNode> Rank(
        IReadOnlyList<FolderMenuNode> nodes,
        IReadOnlyDictionary<string, UsageEntry> entries)
    {
        return nodes
            .Select(node => node with { Children = Rank(node.Children, entries) })
            .OrderByDescending(node => GetUsage(node.RelativePath, entries).Count)
            .ThenByDescending(node => GetUsage(node.RelativePath, entries).LastUsedUtcTicks)
            .ThenBy(node => node.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static UsageEntry GetUsage(
        string relativePath,
        IReadOnlyDictionary<string, UsageEntry> entries) =>
        entries.TryGetValue(relativePath, out var usage)
            ? usage
            : UsageEntry.Empty;

    internal static IReadOnlyList<string> GetPrefixes(string relativeDestination)
    {
        var normalized = CompanyArchive.NormalizeRelativePath(relativeDestination);
        if (normalized == ".") return [];

        var segments = normalized.Split(
            Path.DirectorySeparatorChar,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var prefixes = new string[segments.Length];
        var current = string.Empty;
        for (var index = 0; index < segments.Length; index++)
        {
            current = index == 0
                ? segments[index]
                : Path.Combine(current, segments[index]);
            prefixes[index] = current;
        }

        return prefixes;
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_storagePath)) return;
            var json = File.ReadAllText(_storagePath);
            using var parsed = JsonDocument.Parse(json);
            if (!parsed.RootElement.TryGetProperty("Version", out var versionElement) ||
                !versionElement.TryGetInt32(out var version))
                return;

            if (version == 1)
            {
                var legacy = JsonSerializer.Deserialize<LegacyUsageDocument>(json);
                if (legacy?.Entries is not null)
                    AddProfile(CompanyArchive.DefaultRoot, legacy.Entries);
                return;
            }

            if (version != CurrentVersion) return;
            var document = JsonSerializer.Deserialize<UsageDocument>(json);
            if (document?.Roots is null) return;
            foreach (var (root, entries) in document.Roots)
                AddProfile(root, entries);
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                JsonException or
                System.Security.SecurityException)
        {
            // Frequency data is optional.  A damaged or inaccessible file must
            // never prevent the organizer from starting or moving files.
        }
    }

    private void SaveUnsafe()
    {
        var temporaryPath = _storagePath + ".tmp";
        try
        {
            var directory = Path.GetDirectoryName(_storagePath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            var roots = _profiles.ToDictionary(
                pair => pair.Key,
                pair => new Dictionary<string, UsageEntry>(
                    pair.Value,
                    StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);
            var document = new UsageDocument(CurrentVersion, roots);
            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporaryPath, _storagePath, true);
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                System.Security.SecurityException)
        {
            try
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
            catch
            {
            }
        }
    }

    private void AddProfile(string rootPath, Dictionary<string, UsageEntry> source)
    {
        try
        {
            var root = CompanyArchive.NormalizeRoot(rootPath);
            var entries = new Dictionary<string, UsageEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var (path, usage) in source)
            {
                if (string.IsNullOrWhiteSpace(path) || usage.Count <= 0) continue;
                entries[path] = usage;
            }

            if (entries.Count > 0) _profiles[root] = entries;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
        }
    }

    private sealed record UsageDocument(
        int Version,
        Dictionary<string, Dictionary<string, UsageEntry>> Roots);

    private sealed record LegacyUsageDocument(
        int Version,
        Dictionary<string, UsageEntry> Entries);

    private sealed record UsageEntry(long Count, long LastUsedUtcTicks)
    {
        public static UsageEntry Empty { get; } = new(0, 0);
    }
}
