using GongSiOrganizer.RadialPrototype.Services;
using GongSiOrganizer.Core;
using Xunit;

namespace GongSiOrganizer.Core.Tests.Radial;

public sealed class ArchiveRootStoreTests
{
    [Fact]
    public void SaveAndLoad_PersistsTheNormalizedRoot()
    {
        using var workspace = new TestWorkspace();
        var archiveRoot = workspace.DirectoryAt("archive");
        var storagePath = Path.Combine(workspace.Root, "settings.json");
        var store = new ArchiveRootStore(storagePath);

        Assert.True(store.Save(archiveRoot + Path.DirectorySeparatorChar));

        Assert.Equal(archiveRoot, new ArchiveRootStore(storagePath).Load());
    }

    [Fact]
    public void Save_ReplacesThePreviousRoot()
    {
        using var workspace = new TestWorkspace();
        var firstRoot = workspace.DirectoryAt("first");
        var secondRoot = workspace.DirectoryAt("second");
        var storagePath = Path.Combine(workspace.Root, "settings.json");
        var store = new ArchiveRootStore(storagePath);
        Assert.True(store.Save(firstRoot));

        Assert.True(store.Save(secondRoot));

        Assert.Equal(secondRoot, new ArchiveRootStore(storagePath).Load());
    }

    [Fact]
    public void CorruptSettings_AreIgnored()
    {
        using var workspace = new TestWorkspace();
        var storagePath = workspace.FileAt("{ not-json", "settings.json");

        Assert.Null(new ArchiveRootStore(storagePath).Load());
    }

    [Fact]
    public void UnsupportedSettingsVersion_IsIgnored()
    {
        using var workspace = new TestWorkspace();
        var storagePath = workspace.FileAt(
            """{ "Version": 99, "ArchiveRoot": "E:\\\\GongSi" }""",
            "settings.json");

        Assert.Null(new ArchiveRootStore(storagePath).Load());
    }

    [Fact]
    public void MarkOnboardingCompleted_PersistsAndKeepsTheCurrentRoot()
    {
        using var workspace = new TestWorkspace();
        var archiveRoot = workspace.DirectoryAt("archive");
        var storagePath = Path.Combine(workspace.Root, "settings.json");
        var store = new ArchiveRootStore(storagePath);

        Assert.True(store.MarkOnboardingCompleted(archiveRoot));

        var restored = new ArchiveRootStore(storagePath);
        Assert.True(restored.IsOnboardingCompleted());
        Assert.Equal(archiveRoot, restored.Load());
    }

    [Fact]
    public void ChangingRoot_DoesNotResetOnboardingCompletion()
    {
        using var workspace = new TestWorkspace();
        var firstRoot = workspace.DirectoryAt("first");
        var secondRoot = workspace.DirectoryAt("second");
        var storagePath = Path.Combine(workspace.Root, "settings.json");
        var store = new ArchiveRootStore(storagePath);
        Assert.True(store.MarkOnboardingCompleted(firstRoot));

        Assert.True(store.Save(secondRoot));

        var restored = new ArchiveRootStore(storagePath);
        Assert.True(restored.IsOnboardingCompleted());
        Assert.Equal(secondRoot, restored.Load());
    }

    [Fact]
    public void VersionOneSettings_KeepTheRootAndShowTheNewGuideOnce()
    {
        using var workspace = new TestWorkspace();
        var archiveRoot = workspace.DirectoryAt("legacy");
        var storagePath = workspace.FileAt(
            $$"""{ "Version": 1, "ArchiveRoot": "{{archiveRoot.Replace("\\", "\\\\")}}" }""",
            "settings.json");

        var restored = new ArchiveRootStore(storagePath);

        Assert.Equal(archiveRoot, restored.Load());
        Assert.False(restored.IsOnboardingCompleted());
    }

    [Fact]
    public void SaveAndLoad_PreservesTheThisPcVirtualRoot()
    {
        using var workspace = new TestWorkspace();
        var storagePath = Path.Combine(workspace.Root, "settings.json");
        var store = new ArchiveRootStore(storagePath);

        Assert.True(store.Save(ArchiveScope.ThisPcRoot));

        Assert.Equal(
            ArchiveScope.ThisPcRoot,
            new ArchiveRootStore(storagePath).Load());
    }

    [Fact]
    public void VersionThreeFolderRoot_MigratesToThisPcWithADefaultStart()
    {
        using var workspace = new TestWorkspace();
        var archiveRoot = workspace.DirectoryAt("legacy");
        var storagePath = workspace.FileAt(
            $$"""
            {
              "Version": 3,
              "ArchiveRoot": "{{archiveRoot.Replace("\\", "\\\\")}}",
              "OnboardingCompleted": true
            }
            """,
            "settings.json");

        var restored = new ArchiveRootStore(storagePath);
        var profile = Assert.IsType<ArchiveNavigationProfile>(
            restored.LoadProfile());

        Assert.Equal(ArchiveScope.ThisPcRoot, profile.RootPath);
        Assert.NotNull(profile.StartPath);
        Assert.Equal(
            Path.TrimEndingDirectorySeparator(archiveRoot),
            Path.TrimEndingDirectorySeparator(
                ArchiveScope.GetNavigationSelectionPath(profile)),
            ignoreCase: true);
        Assert.True(restored.IsOnboardingCompleted());
    }

    [Fact]
    public void UnifiedProfile_PersistsTheDefaultStartToken()
    {
        using var workspace = new TestWorkspace();
        var storagePath = Path.Combine(workspace.Root, "settings.json");
        var profile = ArchiveScope.CreateNavigationProfile(
            workspace.DirectoryAt("默认起点"));
        var store = new ArchiveRootStore(storagePath);

        Assert.True(store.Save(profile));

        Assert.Equal(profile, new ArchiveRootStore(storagePath).LoadProfile());
    }
}
