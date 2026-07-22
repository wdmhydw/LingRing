using System.IO;
using System.Text.Json;
using GongSiOrganizer.Core;

namespace GongSiOrganizer.RadialPrototype.Services;

internal sealed class ArchiveRootStore
{
    private const int CurrentVersion = 2;
    private readonly object _gate = new();
    private readonly string _storagePath;

    internal ArchiveRootStore(string storagePath)
    {
        _storagePath = Path.GetFullPath(storagePath);
    }

    public static ArchiveRootStore CreateDefault() =>
        new(OrganizerDataPath.ForFile("settings.json"));

    public string? Load()
    {
        lock (_gate)
        {
            return ReadUnsafe().ArchiveRoot;
        }
    }

    public bool IsOnboardingCompleted()
    {
        lock (_gate)
        {
            return ReadUnsafe().OnboardingCompleted;
        }
    }

    public bool Save(string rootPath)
    {
        var root = CompanyArchive.NormalizeRoot(rootPath);
        lock (_gate)
        {
            var settings = ReadUnsafe() with { ArchiveRoot = root };
            return WriteUnsafe(settings);
        }
    }

    public bool MarkOnboardingCompleted(string? currentRoot)
    {
        lock (_gate)
        {
            var settings = ReadUnsafe();
            var normalizedRoot = !string.IsNullOrWhiteSpace(currentRoot)
                ? CompanyArchive.NormalizeRoot(currentRoot)
                : settings.ArchiveRoot;
            return WriteUnsafe(settings with
            {
                ArchiveRoot = normalizedRoot,
                OnboardingCompleted = true
            });
        }
    }

    private StoredSettings ReadUnsafe()
    {
        try
        {
            if (!File.Exists(_storagePath)) return StoredSettings.Empty;
            var json = File.ReadAllText(_storagePath);
            using var parsed = JsonDocument.Parse(json);
            if (!parsed.RootElement.TryGetProperty("Version", out var versionElement) ||
                !versionElement.TryGetInt32(out var version))
                return StoredSettings.Empty;

            if (version == 1)
            {
                var legacy = JsonSerializer.Deserialize<LegacySettingsDocument>(json);
                return new StoredSettings(NormalizeOrNull(legacy?.ArchiveRoot), false);
            }

            if (version != CurrentVersion) return StoredSettings.Empty;
            var document = JsonSerializer.Deserialize<SettingsDocument>(json);
            return document is null
                ? StoredSettings.Empty
                : new StoredSettings(
                    NormalizeOrNull(document.ArchiveRoot),
                    document.OnboardingCompleted);
        }
        catch (Exception exception) when (IsStorageException(exception))
        {
            return StoredSettings.Empty;
        }
    }

    private bool WriteUnsafe(StoredSettings settings)
    {
        var temporaryPath = _storagePath + ".tmp";
        try
        {
            var directory = Path.GetDirectoryName(_storagePath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(
                    new SettingsDocument(
                        CurrentVersion,
                        settings.ArchiveRoot,
                        settings.OnboardingCompleted),
                    new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporaryPath, _storagePath, true);
            return true;
        }
        catch (Exception exception) when (IsStorageException(exception))
        {
            try
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
            catch
            {
            }

            return false;
        }
    }

    private static string? NormalizeOrNull(string? rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath)) return null;
        try
        {
            return CompanyArchive.NormalizeRoot(rootPath);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static bool IsStorageException(Exception exception) =>
        exception is IOException or
            UnauthorizedAccessException or
            JsonException or
            ArgumentException or
            NotSupportedException or
            System.Security.SecurityException;

    private sealed record SettingsDocument(
        int Version,
        string? ArchiveRoot,
        bool OnboardingCompleted);

    private sealed record LegacySettingsDocument(int Version, string? ArchiveRoot);

    private sealed record StoredSettings(string? ArchiveRoot, bool OnboardingCompleted)
    {
        public static StoredSettings Empty { get; } = new(null, false);
    }
}
