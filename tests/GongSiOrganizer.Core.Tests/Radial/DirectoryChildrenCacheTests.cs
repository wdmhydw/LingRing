using GongSiOrganizer.RadialPrototype.Services;
using Xunit;

namespace GongSiOrganizer.Core.Tests.Radial;

public sealed class DirectoryChildrenCacheTests
{
    [Theory]
    [InlineData(3L * 1024 * 1024 * 1024, 4L * 1024 * 1024)]
    [InlineData(6L * 1024 * 1024 * 1024, 8L * 1024 * 1024)]
    [InlineData(12L * 1024 * 1024 * 1024, 16L * 1024 * 1024)]
    [InlineData(32L * 1024 * 1024 * 1024, 24L * 1024 * 1024)]
    public void AdaptivePolicy_ScalesWithinAConservativeMemoryBudget(
        long availableMemory,
        long expectedMaximumBytes)
    {
        var policy = DirectoryCachePolicy.CreateAdaptive(availableMemory);

        Assert.Equal(expectedMaximumBytes, policy.MaximumEstimatedBytes);
        Assert.Equal(2, policy.MaximumConcurrentReads);
        Assert.InRange(policy.MaximumParents, 256, 1024);
    }

    [Fact]
    public async Task Cache_StoresOnlyImmediateVisibleChildFolders()
    {
        using var workspace = new TestWorkspace();
        var parent = workspace.DirectoryAt("parent");
        workspace.DirectoryAt("parent", "甲");
        var hidden = workspace.DirectoryAt("parent", "隐藏");
        File.SetAttributes(hidden, File.GetAttributes(hidden) | FileAttributes.Hidden);
        workspace.DirectoryAt("parent", "甲", "下一级");
        using var cache = new DirectoryChildrenCache(
            new DirectoryCachePolicy(8, 1024 * 1024, TimeSpan.FromMinutes(2), 2));

        var children = await cache.GetChildrenAsync(parent);

        Assert.Single(children);
        Assert.Equal("甲", children[0].Name);
    }

    [Fact]
    public async Task Cache_EvictsLeastRecentlyUsedParentsAtTheConfiguredLimit()
    {
        using var workspace = new TestWorkspace();
        var first = workspace.DirectoryAt("first");
        var second = workspace.DirectoryAt("second");
        var third = workspace.DirectoryAt("third");
        using var cache = new DirectoryChildrenCache(
            new DirectoryCachePolicy(2, long.MaxValue, TimeSpan.FromMinutes(2), 2));

        _ = await cache.GetChildrenAsync(first);
        _ = await cache.GetChildrenAsync(second);
        _ = await cache.GetChildrenAsync(third);

        Assert.InRange(cache.CachedParentCount, 1, 2);
    }
}
