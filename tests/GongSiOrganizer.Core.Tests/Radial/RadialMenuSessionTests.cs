using GongSiOrganizer.RadialPrototype.Radial;
using Xunit;

namespace GongSiOrganizer.Core.Tests.Radial;

public sealed class RadialMenuSessionTests
{
    [Fact]
    public void Ring_OnlyShowsFourItemsAndWheelCyclesSiblings()
    {
        var roots = Enumerable.Range(1, 6)
            .Select(index => Node($"公司{index}"))
            .ToArray();
        var session = new RadialMenuSession(roots);

        var initial = session.GetRingView(0);
        Assert.Equal(6, initial.TotalCount);
        Assert.Equal(4, initial.Slots.Count(item => item is not null));
        Assert.Equal("公司1", initial.Slots[0]!.DisplayName);

        Assert.True(session.Rotate(0, 1));
        var rotated = session.GetRingView(0);
        Assert.Equal(1, rotated.Offset);
        Assert.Equal("公司2", rotated.Slots[0]!.DisplayName);
    }

    [Fact]
    public void ThirdRing_RemainsEmptyUntilSecondLevelIsChosen()
    {
        var leaf = Node("项目");
        var company = Node("公司", leaf);
        var session = new RadialMenuSession([company]);

        Assert.True(session.Hover(0, 0));

        Assert.Equal("项目", session.GetRingView(1).Slots[0]!.DisplayName);
        Assert.Equal(0, session.GetRingView(2).TotalCount);
    }

    [Fact]
    public void DeepNavigation_KeepsTopPreviousAndCurrentLayers()
    {
        var e = Node("第五层");
        var d = Node("第四层", e);
        var c = Node("第三层", d);
        var b = Node("第二层", c);
        var a = Node("最高层", b);
        var session = new RadialMenuSession([a]);

        Assert.True(session.Hover(0, 0));
        Assert.True(session.Hover(1, 0));
        Assert.True(session.Hover(2, 0));
        Assert.True(session.Hover(2, 0));
        Assert.True(session.Hover(2, 0));

        Assert.Equal(5, session.Path.Count);
        Assert.Equal("最高层 / … / 第四层 / 第五层", session.Breadcrumb);
        Assert.Equal("最高层", session.GetRingView(0).SelectedRelativePath);
        Assert.Equal(
            "最高层\\第二层\\第三层\\第四层",
            session.GetRingView(1).SelectedRelativePath);
        Assert.Equal(
            "最高层\\第二层\\第三层\\第四层\\第五层",
            session.GetRingView(2).SelectedRelativePath);
        Assert.Equal(1, session.GetRingView(2).TotalCount);
        Assert.Equal("最高层\\第二层\\第三层\\第四层\\第五层", session.CurrentDestination!.RelativePath);
    }

    [Fact]
    public void WheelSelection_ImmediatelyRefreshesTheChildRing()
    {
        var children = Enumerable.Range(1, 6)
            .Select(index => Node($"同级{index}", Node($"下级{index}")))
            .ToArray();
        var session = new RadialMenuSession([Node("最高层", children)]);

        Assert.True(session.Hover(0, 0));
        Assert.True(session.Hover(1, 0));
        Assert.Equal("下级1", session.GetRingView(2).Slots[0]!.DisplayName);

        Assert.True(session.RotateAndHover(1, 1, 0));

        Assert.Equal("同级2", session.GetRingView(1).Slots[0]!.DisplayName);
        Assert.Equal("下级2", session.GetRingView(2).Slots[0]!.DisplayName);
        Assert.Equal("最高层\\同级2", session.CurrentDestination!.RelativePath);
    }

    [Fact]
    public void OuterRingWheel_PreviewsSiblingUntilHoverCommitsTheNextLevel()
    {
        var siblings = Enumerable.Range(1, 6)
            .Select(index => Node($"第三级{index}", Node($"第四级{index}")))
            .ToArray();
        var session = new RadialMenuSession(
            [Node("第一级", Node("第二级", siblings))]);
        Assert.True(session.Hover(0, 0));
        Assert.True(session.Hover(1, 0));

        Assert.True(session.RotateAndPreview(2, 1, 0));

        Assert.Equal(2, session.Path.Count);
        Assert.Equal("第二级", session.CurrentDestination!.DisplayName);
        Assert.Equal(
            @"第一级\第二级",
            session.GetRingView(1).SelectedRelativePath);
        Assert.Null(session.GetRingView(2).SelectedRelativePath);
        Assert.Equal("第三级2", session.GetRingView(2).Slots[0]!.DisplayName);

        Assert.True(session.Hover(2, 0));

        Assert.Equal(
            @"第一级\第二级\第三级2",
            session.GetRingView(1).SelectedRelativePath);
        Assert.Equal("第四级2", session.GetRingView(2).Slots[0]!.DisplayName);
    }

    [Fact]
    public async Task OuterRingWheel_DoesNotAdvanceWhenPreviewedChildrenFinishLoading()
    {
        var pending = new TaskCompletionSource<IReadOnlyList<GongSiOrganizer.Core.FolderMenuNode>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var previewChildren = new GongSiOrganizer.Core.AsyncFolderMenuNodeList(
            () => pending.Task);
        var siblings = Enumerable.Range(1, 6)
            .Select(index => index == 2
                ? new GongSiOrganizer.Core.FolderMenuNode(
                    "第三级2",
                    @"第一级\第二级\第三级2",
                    previewChildren)
                : new GongSiOrganizer.Core.FolderMenuNode(
                    $"第三级{index}",
                    $@"第一级\第二级\第三级{index}",
                    []))
            .ToArray();
        var session = new RadialMenuSession(
        [
            new(
                "第一级",
                "第一级",
                [
                    new(
                        "第二级",
                        @"第一级\第二级",
                        siblings)
                ])
        ]);
        Assert.True(session.Hover(0, 0));
        Assert.True(session.Hover(1, 0));
        Assert.True(session.RotateAndPreview(2, 1, 0));
        var changed = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        session.Changed += (_, _) => changed.TrySetResult();

        pending.SetResult(
        [
            new(
                "第四级2",
                @"第一级\第二级\第三级2\第四级2",
                [])
        ]);
        await changed.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal("第三级2", session.GetRingView(2).Slots[0]!.DisplayName);
        Assert.Equal(
            @"第一级\第二级",
            session.GetRingView(1).SelectedRelativePath);
        Assert.True(session.Hover(2, 0));
        Assert.Equal("第四级2", session.GetRingView(2).Slots[0]!.DisplayName);
    }

    [Fact]
    public void NavigateBack_RestoresTheParentAndKeepsTheFormerChoiceVisible()
    {
        var leaf = Node("第五层");
        var fourth = Node("第四层", leaf);
        var third = Node("第三层", fourth);
        var second = Node("第二层", third);
        var top = Node("最高层", second);
        var session = new RadialMenuSession([top]);

        Assert.True(session.Hover(0, 0));
        Assert.True(session.Hover(1, 0));
        Assert.True(session.Hover(2, 0));
        Assert.True(session.Hover(2, 0));
        Assert.True(session.Hover(2, 0));
        Assert.Equal("第五层", session.CurrentDestination!.DisplayName);

        Assert.True(session.NavigateBack());

        Assert.Equal("第四层", session.CurrentDestination!.DisplayName);
        Assert.Equal("第四层", session.GetRingView(1).Slots[0]!.DisplayName);
        Assert.Equal("第五层", session.GetRingView(2).Slots[0]!.DisplayName);
        Assert.Null(session.GetRingView(2).SelectedRelativePath);

        Assert.True(session.NavigateBack());
        Assert.True(session.NavigateBack());
        Assert.True(session.NavigateBack());
        Assert.True(session.NavigateBack());
        Assert.Empty(session.Path);
        Assert.False(session.NavigateBack());
    }

    [Fact]
    public void WheelDoesNothingWhenThereAreFourOrFewerSiblings()
    {
        var session = new RadialMenuSession([Node("一"), Node("二"), Node("三"), Node("四")]);

        Assert.False(session.Rotate(0, 1));
        Assert.Equal(0, session.GetRingView(0).Offset);
    }

    [Fact]
    public void SelectPath_ReopensADeepLeafAsTheCurrentDestination()
    {
        var tree = Node("最高层", Node("第二层", Node("新文件夹")));
        var session = new RadialMenuSession([tree]);

        Assert.True(session.SelectPath(@"最高层\第二层\新文件夹"));

        Assert.Equal("新文件夹", session.CurrentDestination!.DisplayName);
        Assert.Equal("最高层", session.GetRingView(0).SelectedRelativePath);
        Assert.Equal(@"最高层\第二层", session.GetRingView(1).SelectedRelativePath);
        Assert.Equal(@"最高层\第二层\新文件夹", session.GetRingView(2).SelectedRelativePath);
    }

    [Fact]
    public void SelectPath_MissingTargetDoesNotDiscardTheExistingSelection()
    {
        var session = new RadialMenuSession([Node("公司甲")]);
        Assert.True(session.Hover(0, 0));

        Assert.False(session.SelectPath("不存在"));

        Assert.Equal("公司甲", session.CurrentDestination!.DisplayName);
    }

    [Fact]
    public async Task AsyncChildren_LoadWithoutBlockingAndAppearWhenReady()
    {
        var pending = new TaskCompletionSource<IReadOnlyList<GongSiOrganizer.Core.FolderMenuNode>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var children = new GongSiOrganizer.Core.AsyncFolderMenuNodeList(
            () => pending.Task);
        var drive = new GongSiOrganizer.Core.FolderMenuNode("C: 本地磁盘", "C", children);
        var session = new RadialMenuSession([drive]);
        var changed = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        session.Changed += (_, _) => changed.TrySetResult();

        Assert.True(session.Hover(0, 0));
        Assert.True(session.IsCurrentChildrenLoading);
        Assert.Equal(0, session.GetRingView(1).TotalCount);

        pending.SetResult(
            [new GongSiOrganizer.Core.FolderMenuNode("资料", @"C\资料", [])]);
        await changed.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(session.IsCurrentChildrenLoading);
        Assert.Equal("资料", session.GetRingView(1).Slots[0]!.DisplayName);
    }

    [Fact]
    public async Task SelectPathAsync_LoadsOnlyTheRequestedChain()
    {
        var projectChildren = new GongSiOrganizer.Core.AsyncFolderMenuNodeList(
            () => Task.FromResult<IReadOnlyList<GongSiOrganizer.Core.FolderMenuNode>>(
            [
                new("项目A", @"C\资料\项目A", [])
            ]));
        var driveChildren = new GongSiOrganizer.Core.AsyncFolderMenuNodeList(
            () => Task.FromResult<IReadOnlyList<GongSiOrganizer.Core.FolderMenuNode>>(
            [
                new("资料", @"C\资料", projectChildren)
            ]));
        var session = new RadialMenuSession(
            [new GongSiOrganizer.Core.FolderMenuNode("C: 本地磁盘", "C", driveChildren)]);

        Assert.True(await session.SelectPathAsync(@"C\资料\项目A"));

        Assert.Equal("项目A", session.CurrentDestination!.DisplayName);
        Assert.Equal(3, session.Path.Count);
    }

    [Fact]
    public async Task DefaultFolderStart_CanReturnThroughDriveToThisPc()
    {
        var folderChildren = new GongSiOrganizer.Core.AsyncFolderMenuNodeList(
            () => Task.FromResult<IReadOnlyList<GongSiOrganizer.Core.FolderMenuNode>>(
            [
                new("项目A", @"C\资料\项目A", [])
            ]));
        var driveChildren = new GongSiOrganizer.Core.AsyncFolderMenuNodeList(
            () => Task.FromResult<IReadOnlyList<GongSiOrganizer.Core.FolderMenuNode>>(
            [
                new("资料", @"C\资料", folderChildren)
            ]));
        var session = new RadialMenuSession(
            [new GongSiOrganizer.Core.FolderMenuNode("C: 本地磁盘", "C", driveChildren)]);
        Assert.True(await session.SelectStartPathAsync(@"C\资料"));

        Assert.Equal(@"C\资料", session.CurrentDestination!.RelativePath);
        Assert.Equal("项目A", session.GetRingView(0).Slots[0]!.DisplayName);
        Assert.DoesNotContain(
            session.GetRingView(0).Slots,
            item => item?.RelativePath == "C");
        Assert.True(session.NavigateBack());
        Assert.Equal("C", session.CurrentDestination!.RelativePath);
        Assert.Equal("资料", session.GetRingView(0).Slots[0]!.DisplayName);
        Assert.True(session.NavigateBack());
        Assert.Empty(session.Path);
        Assert.Equal("C: 本地磁盘", session.GetRingView(0).Slots[0]!.DisplayName);
    }

    [Fact]
    public async Task SelectedChild_RemainsInsideTheConfiguredVisibleAnchor()
    {
        var folderChildren = new GongSiOrganizer.Core.AsyncFolderMenuNodeList(
            () => Task.FromResult<IReadOnlyList<GongSiOrganizer.Core.FolderMenuNode>>(
            [
                new("项目A", @"C\资料\项目A", [])
            ]));
        var driveChildren = new GongSiOrganizer.Core.AsyncFolderMenuNodeList(
            () => Task.FromResult<IReadOnlyList<GongSiOrganizer.Core.FolderMenuNode>>(
            [
                new("资料", @"C\资料", folderChildren)
            ]));
        var session = new RadialMenuSession(
            [new GongSiOrganizer.Core.FolderMenuNode("C: 本地磁盘", "C", driveChildren)]);

        Assert.True(await session.SelectPathAsync(
            @"C\资料\项目A",
            @"C\资料"));

        Assert.Equal(@"C\资料\项目A", session.CurrentDestination!.RelativePath);
        Assert.Equal(
            @"C\资料\项目A",
            session.GetRingView(0).SelectedRelativePath);
        Assert.DoesNotContain(
            session.GetRingView(0).Slots,
            item => item?.RelativePath == "C");
    }

    private static GongSiOrganizer.Core.FolderMenuNode Node(
        string name,
        params GongSiOrganizer.Core.FolderMenuNode[] children)
    {
        var childPaths = children
            .Select(child => PrefixPath(child, name))
            .ToArray();
        return new GongSiOrganizer.Core.FolderMenuNode(name, name, childPaths);
    }

    private static GongSiOrganizer.Core.FolderMenuNode PrefixPath(
        GongSiOrganizer.Core.FolderMenuNode node,
        string prefix) =>
        node with
        {
            RelativePath = $"{prefix}\\{node.RelativePath}",
            Children = node.Children.Select(child => PrefixPath(child, prefix)).ToArray()
        };
}
