using System.IO;
using System.Text.Json;
using GongSiOrganizer.Core;

namespace GongSiOrganizer.RadialPrototype.Services;

internal sealed class ArchiveRootStore
{
    private const int CurrentVersion = 4;
    private const int InteractionGuideRevision = 3;
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
            var profile = ReadUnsafe().Profile;
            if (profile is null) return null;
            try
            {
                return ArchiveScope.GetNavigationSelectionPath(profile);
            }
            catch (Exception exception) when (IsStorageException(exception))
            {
                return profile.RootPath;
            }
        }
    }

    public ArchiveNavigationProfile? LoadProfile()
    {
        lock (_gate)
        {
            return ReadUnsafe().Profile;
        }
    }

    public bool IsOnboardingCompleted()
    {
        lock (_gate)
        {
            return ReadUnsafe().OnboardingCompleted;
        }
    }

    public bool NeedsInteractionGuide()
    {
        lock (_gate)
        {
            var settings = ReadUnsafe();
            return !settings.OnboardingCompleted || settings.OnboardingRevision < InteractionGuideRevision;
        }
    }

    public bool Save(string rootPath)
    {
        return Save(ArchiveScope.CreateNavigationProfile(rootPath));
    }

    public bool Save(ArchiveNavigationProfile profile)
    {
        var normalized = ArchiveScope.NormalizeNavigationProfile(
            profile.RootPath,
            profile.StartPath);
        lock (_gate)
        {
            var settings = ReadUnsafe() with { Profile = normalized };
            return WriteUnsafe(settings);
        }
    }

    public bool MarkOnboardingCompleted(string? currentRoot)
    {
        ArchiveNavigationProfile? profile = null;
        if (!string.IsNullOrWhiteSpace(currentRoot))
            profile = ArchiveScope.CreateNavigationProfile(currentRoot);
        return MarkOnboardingCompleted(profile);
    }

    public bool MarkOnboardingCompleted(ArchiveNavigationProfile? currentProfile)
    {
        lock (_gate)
        {
            var settings = ReadUnsafe();
            var normalizedProfile = currentProfile is not null
                ? ArchiveScope.NormalizeNavigationProfile(
                    currentProfile.RootPath,
                    currentProfile.StartPath)
                : settings.Profile;
            return WriteUnsafe(settings with
            {
                Profile = normalizedProfile,
                OnboardingCompleted = true,
                OnboardingRevision = InteractionGuideRevision
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
                return new StoredSettings(
                    NormalizeProfileOrNull(legacy?.ArchiveRoot, null),
                    false);
            }

            if (version is 2 or 3)
            {
                var legacy = JsonSerializer.Deserialize<LegacyCompletedSettingsDocument>(json);
                return legacy is null
                    ? StoredSettings.Empty
                    : new StoredSettings(
                        NormalizeProfileOrNull(legacy.ArchiveRoot, null),
                        legacy.OnboardingCompleted);
            }

            if (version != CurrentVersion) return StoredSettings.Empty;
            var document = JsonSerializer.Deserialize<SettingsDocument>(json);
            return document is null
                ? StoredSettings.Empty
                : new StoredSettings(
                    NormalizeProfileOrNull(
                        document.ArchiveRoot,
                        document.StartPath),
                    document.OnboardingCompleted,
                    document.OnboardingRevision);
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
                        settings.Profile?.RootPath,
                        settings.Profile?.StartPath,
                        settings.OnboardingCompleted,
                        settings.OnboardingRevision),
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

    private static ArchiveNavigationProfile? NormalizeProfileOrNull(
        string? rootPath,
        string? startPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath)) return null;
        try
        {
            return ArchiveScope.NormalizeNavigationProfile(rootPath, startPath);
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
            InvalidOperationException or
            NotSupportedException or
            System.Security.SecurityException;

    private sealed record SettingsDocument(
        int Version,
        string? ArchiveRoot,
        string? StartPath,
        bool OnboardingCompleted,
        int OnboardingRevision = 0);

    private sealed record LegacySettingsDocument(int Version, string? ArchiveRoot);

    private sealed record LegacyCompletedSettingsDocument(
        int Version,
        string? ArchiveRoot,
        bool OnboardingCompleted);

    private sealed record StoredSettings(
        ArchiveNavigationProfile? Profile,
        bool OnboardingCompleted,
        int OnboardingRevision = 0)
    {
        public static StoredSettings Empty { get; } = new(null, false);
    }
}
