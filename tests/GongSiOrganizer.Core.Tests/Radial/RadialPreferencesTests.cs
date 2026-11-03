using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows.Media;
using GongSiOrganizer.RadialPrototype.Radial;
using GongSiOrganizer.RadialPrototype.Services;
using Xunit;

namespace GongSiOrganizer.Core.Tests.Radial;

public sealed class RadialPreferencesTests
{
    [Fact]
    public void NewAndUpgradedUsersStartWithFourCurvedLabels()
    {
        using var workspace = new TestWorkspace();
        workspace.FileAt("""{"Version":4,"ArchiveRoot":"shell:ThisPC","OnboardingCompleted":true,"OnboardingRevision":3}""", "settings.json");
        var preferences = new RadialPreferencesStore(Path.Combine(workspace.Root, "wheel-settings.json")).Load();
        Assert.Equal(4, preferences.MaximumItems);
        Assert.True(preferences.CurvedLabels);
        Assert.Equal(RadialLabelDirection.Arc, preferences.DisplayOptions.LabelDirection);
        Assert.True(preferences.DisplayOptions.EnhancedLabels);
        var archive = new ArchiveRootStore(Path.Combine(workspace.Root, "settings.json"));
        Assert.True(archive.NeedsInteractionGuide());
        Assert.True(archive.MarkOnboardingCompleted(archive.LoadProfile()));
        Assert.False(archive.NeedsInteractionGuide());
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(4, true)]
    [InlineData(5, false)]
    [InlineData(6, true)]
    [InlineData(8, false)]
    public void ChoiceSurvivesRestartWithoutTouchingArchiveSettings(int count, bool curved)
    {
        using var workspace = new TestWorkspace();
        var archivePath = workspace.FileAt("archive settings must stay intact", "settings.json");
        var path = Path.Combine(workspace.Root, "wheel-settings.json");
        var expected = new RadialPreferences(count, curved);
        Assert.True(new RadialPreferencesStore(path).Save(expected));
        var restored = new RadialPreferencesStore(path).Load();
        Assert.Equal(expected, restored);
        Assert.Equal(curved, restored.DisplayOptions.EnhancedLabels);
        Assert.Equal(curved ? 82 : 60, restored.DisplayOptions.RingWidth);
        Assert.Equal("archive settings must stay intact", File.ReadAllText(archivePath));
    }

    [Theory]
    [InlineData("broken json", 4)]
    [InlineData("{\"Version\":2,\"Preferences\":{\"MaximumItems\":8}}", 4)]
    [InlineData("{\"Version\":1,\"Preferences\":{\"MaximumItems\":99}}", 8)]
    [InlineData("{\"Version\":1,\"Preferences\":{\"MaximumItems\":0}}", 2)]
    public void DamagedOrOutOfRangeSettingsHaveUsableDefaults(string json, int expected)
    {
        using var workspace = new TestWorkspace();
        var path = workspace.FileAt(json, "wheel-settings.json");
        Assert.Equal(expected, new RadialPreferencesStore(path).Load().MaximumItems);
    }

    [Fact]
    public void FailedSaveKeepsPreviousChoice()
    {
        using var workspace = new TestWorkspace();
        var path = Path.Combine(workspace.Root, "wheel-settings.json");
        var store = new RadialPreferencesStore(path);
        Assert.True(store.Save(new(6, false)));
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.False(store.Save(new(8, true)));
        Assert.Equal(new RadialPreferences(6, false), store.Load());
    }

    [Theory]
    [InlineData(2, true)]
    [InlineData(4, true)]
    [InlineData(6, false)]
    [InlineData(8, true)]
    public void ProductionOverlayUsesChoiceForEveryEntryPath(int count, bool curved)
    {
        RunOnSta(() =>
        {
            var roots = Enumerable.Range(0, 12).Select(i => new FolderMenuNode($"项目 {i}", $"item{i}", [])).ToArray();
            var snapshot = new FolderCatalogResult("C:\\", roots, roots.Length, 0);
            var window = new RadialOverlayWindow();
            try
            {
                window.SetPreferences(new(count, curved));
                window.PrepareForFirstShow(snapshot);
                var point = NativeDisplay.GetPrimaryWorkAreaCenter();
                window.ShowMenu(point, ["sample.txt"], snapshot);
                Check();
                window.ShowMenuAsync(point, ["sample.txt"], snapshot, "item10", CancellationToken.None).GetAwaiter().GetResult();
                Check(emptyStart: true);
                window.ShowDemoAsync(point, snapshot, null).GetAwaiter().GetResult();
                Check();
                window.RefreshMenuAsync(snapshot, "item3").GetAwaiter().GetResult();
                Check();
                window.SetPreferences(new(4, !curved));
                Assert.False(window.IsVisible);
                window.ShowMenu(point, ["sample.txt"], snapshot);
                Assert.Equal(!curved, Control().DisplayOptions.EnhancedLabels);

                RadialMenuControl Control() => (RadialMenuControl)VisualTreeHelper.GetChild((Visual)window.Content, 0);
                void Check(bool emptyStart = false)
                {
                    var control = Control();
                    var session = (RadialMenuSession)typeof(RadialMenuControl).GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(control)!;
                    Assert.All(Enumerable.Range(0, 3), ring => Assert.Equal(count, session.GetCapacity(ring)));
                    Assert.Equal(curved, control.DisplayOptions.EnhancedLabels);
                    if (emptyStart)
                    {
                        Assert.Empty(session.GetItemsForRing(0));
                        Assert.Equal("item10", session.CurrentDestination!.RelativePath);
                    }
                    else
                    {
                        Assert.True(session.Rotate(0, 1));
                        Assert.NotNull(session.GetFolder(0, count - 1));
                    }
                }
            }
            finally { window.Close(); }
        });
    }

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception e) { failure = e; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
