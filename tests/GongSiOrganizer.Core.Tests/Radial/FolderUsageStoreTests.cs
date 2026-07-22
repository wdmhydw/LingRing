using GongSiOrganizer.Core;
using GongSiOrganizer.RadialPrototype.Services;
using Xunit;

namespace GongSiOrganizer.Core.Tests.Radial;

public sealed class FolderUsageStoreTests
{
    private const string DefaultRoot = @"E:\GongSi";

    [Fact]
    public void GetPrefixes_ReturnsEveryLevelExactlyOnce()
    {
        var prefixes = FolderUsageStore.GetPrefixes(@"公司甲\项目A\资料");

        Assert.Equal(
            [@"公司甲", @"公司甲\项目A", @"公司甲\项目A\资料"],
            prefixes);
    }

    [Fact]
    public void Apply_PromotesFrequentlyChosenBranchesWithinEachSiblingLevel()
    {
        using var workspace = new TestWorkspace();
        var store = new FolderUsageStore(Path.Combine(workspace.Root, "usage.json"));
        var catalog = Catalog(
            Node("公司乙"),
            Node("公司甲", Node("项目B"), Node("项目A")));

        store.RecordSelection(DefaultRoot, @"公司甲\项目A\资料");
        store.RecordSelection(DefaultRoot, @"公司甲\项目A");
        store.RecordSelection(DefaultRoot, "公司乙");

        var ranked = store.Apply(catalog);

        Assert.Equal("公司甲", ranked.Folders[0].DisplayName);
        Assert.Equal("项目A", ranked.Folders[0].Children[0].DisplayName);
        Assert.Equal("公司乙", ranked.Folders[1].DisplayName);
    }

    [Fact]
    public void Usage_PersistsAcrossStoreInstances()
    {
        using var workspace = new TestWorkspace();
        var storagePath = Path.Combine(workspace.Root, "usage.json");
        new FolderUsageStore(storagePath).RecordSelection(DefaultRoot, "公司乙");

        var restored = new FolderUsageStore(storagePath);
        var ranked = restored.Apply(Catalog(Node("公司甲"), Node("公司乙")));

        Assert.Equal("公司乙", ranked.Folders[0].DisplayName);
    }

    [Fact]
    public void CorruptUsageFile_IsIgnoredAndFallsBackToNameOrder()
    {
        using var workspace = new TestWorkspace();
        var storagePath = workspace.FileAt("{ not-json", "usage.json");

        var store = new FolderUsageStore(storagePath);
        var ranked = store.Apply(Catalog(Node("公司乙"), Node("公司甲")));

        Assert.Equal(["公司甲", "公司乙"], ranked.Folders.Select(node => node.DisplayName));
    }

    [Fact]
    public void Clear_RemovesTheLearnedOrder()
    {
        using var workspace = new TestWorkspace();
        var store = new FolderUsageStore(Path.Combine(workspace.Root, "usage.json"));
        var catalog = Catalog(Node("公司甲"), Node("公司乙"));
        store.RecordSelection(DefaultRoot, "公司乙");
        Assert.Equal("公司乙", store.Apply(catalog).Folders[0].DisplayName);

        store.Clear(DefaultRoot);

        Assert.Equal("公司甲", store.Apply(catalog).Folders[0].DisplayName);
    }

    [Fact]
    public void UsageProfiles_AreIsolatedByArchiveRoot()
    {
        using var workspace = new TestWorkspace();
        var firstRoot = workspace.DirectoryAt("first");
        var secondRoot = workspace.DirectoryAt("second");
        var store = new FolderUsageStore(Path.Combine(workspace.Root, "usage.json"));
        store.RecordSelection(firstRoot, "公司乙");
        store.RecordSelection(secondRoot, "公司甲");

        var firstRanking = store.Apply(Catalog(firstRoot, Node("公司甲"), Node("公司乙")));
        var secondRanking = store.Apply(Catalog(secondRoot, Node("公司甲"), Node("公司乙")));

        Assert.Equal("公司乙", firstRanking.Folders[0].DisplayName);
        Assert.Equal("公司甲", secondRanking.Folders[0].DisplayName);
    }

    [Fact]
    public void VersionOneUsage_IsMigratedToTheDefaultRoot()
    {
        using var workspace = new TestWorkspace();
        var storagePath = workspace.FileAt(
            """
            {
              "Version": 1,
              "Entries": {
                "公司乙": { "Count": 4, "LastUsedUtcTicks": 638800000000000000 }
              }
            }
            """,
            "usage.json");

        var restored = new FolderUsageStore(storagePath);
        var ranked = restored.Apply(Catalog(Node("公司甲"), Node("公司乙")));

        Assert.Equal("公司乙", ranked.Folders[0].DisplayName);
    }

    private static FolderCatalogResult Catalog(params FolderMenuNode[] nodes) =>
        Catalog(DefaultRoot, nodes);

    private static FolderCatalogResult Catalog(string root, params FolderMenuNode[] nodes) =>
        new(root, nodes, Count(nodes), 0);

    private static FolderMenuNode Node(string name, params FolderMenuNode[] children)
    {
        var prefixedChildren = children
            .Select(child => PrefixPath(child, name))
            .ToArray();
        return new FolderMenuNode(name, name, prefixedChildren);
    }

    private static FolderMenuNode PrefixPath(FolderMenuNode node, string prefix) =>
        node with
        {
            RelativePath = $"{prefix}\\{node.RelativePath}",
            Children = node.Children.Select(child => PrefixPath(child, prefix)).ToArray()
        };

    private static int Count(IEnumerable<FolderMenuNode> nodes) =>
        nodes.Sum(node => 1 + Count(node.Children));
}
