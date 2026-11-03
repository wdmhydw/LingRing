using System.IO;
using System.Text.Json;
using GongSiOrganizer.RadialPrototype.Radial;

namespace GongSiOrganizer.RadialPrototype.Services;

internal sealed record RadialPreferences(int MaximumItems = 4, bool CurvedLabels = true)
{
    public RadialPreferences Normalize() => this with { MaximumItems = Math.Clamp(MaximumItems, 2, 8) };

    public RadialDisplayOptions DisplayOptions => new(
        Normalize().MaximumItems,
        CurvedLabels ? RadialLabelDirection.Arc : RadialLabelDirection.Horizontal,
        CurvedLabels ? 82 : 60,
        EnhancedLabels: CurvedLabels);
}

internal sealed class RadialPreferencesStore(string storagePath)
{
    public static RadialPreferencesStore CreateDefault() => new(OrganizerDataPath.ForFile("wheel-settings.json"));

    public RadialPreferences Load()
    {
        try
        {
            if (!File.Exists(storagePath)) return new();
            var document = JsonSerializer.Deserialize<Document>(File.ReadAllText(storagePath));
            return document is { Version: 1 } ? (document.Preferences ?? new()).Normalize() : new();
        }
        catch (Exception exception) when (IsStorageException(exception)) { return new(); }
    }

    public bool Save(RadialPreferences preferences)
    {
        var temporary = storagePath + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(storagePath))!);
            File.WriteAllText(temporary, JsonSerializer.Serialize(new Document(1, preferences.Normalize()),
                new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, storagePath, overwrite: true);
            return true;
        }
        catch (Exception exception) when (IsStorageException(exception)) { return false; }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception exception) when (IsStorageException(exception)) { }
        }
    }

    private static bool IsStorageException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or System.Security.SecurityException or JsonException;

    private sealed record Document(int Version, RadialPreferences? Preferences);
}
