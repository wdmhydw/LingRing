namespace GongSiOrganizer.Core;

/// <summary>
/// Describes the hierarchy shown by the radial menu. <see cref="RootPath"/>
/// is the highest reachable boundary; <see cref="StartPath"/> is an optional
/// destination token that is selected when the wheel opens.
/// </summary>
public sealed record ArchiveNavigationProfile(
    string RootPath,
    string? StartPath)
{
    public static ArchiveNavigationProfile WholeComputer { get; } =
        new(ArchiveScope.ThisPcRoot, null);
}
