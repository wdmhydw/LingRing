using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using GongSiOrganizer.Core;
using GongSiOrganizer.RadialPrototype.Radial;
using GongSiOrganizer.RadialPrototype.Services;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using ListBox = System.Windows.Controls.ListBox;
using TextBox = System.Windows.Controls.TextBox;
using Orientation = System.Windows.Controls.Orientation;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Point = System.Windows.Point;
using Panel = System.Windows.Controls.Panel;
using Binding = System.Windows.Data.Binding;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace GongSiOrganizer.RadialPrototype.Presentation;

// Separate demo host: no mouse hook, usage settings, or file-move handler.
internal sealed class ReadabilityLabWindow : Window
{
    private readonly Grid _page = new();
    private readonly Canvas _stage = new() { Background = UiVisuals.Brush(UiVisuals.Canvas), ClipToBounds = true };
    private readonly RadialMenuControl _wheel = new() { IsSelectionDemo = true };
    private readonly ComboBox _quantity = Choice("2 项", "3 项", "4 项", "5 项", "6 项", "7 项", "8 项");
    private readonly ComboBox _direction = Choice("横排 · 保留首尾", "沿弧竖排 · 首尾相接", "自动 · 多显示文字");
    private readonly ComboBox _width = Choice("标准", "舒展", "宽松");
    private readonly ComboBox _sample = Choice("长中文与同名前缀", "中英混排与版本号", "短名称对照");
    private readonly ComboBox _position = Choice("居中", "左下边角", "右上边角");
    private readonly ComboBox _listRing = Choice("跟随鼠标所在圈", "第一圈", "第二圈", "第三圈");
    private readonly CheckBox _adapt = new() { Content = "边角自动减少项目", Margin = new Thickness(0, 12, 0, 10) };
    private readonly CheckBox _companion = new() { Content = "显示完整名称侧栏", Margin = new Thickness(0, 0, 0, 18) };
    private readonly TextBox _search = new() { Height = 34, Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 6, 0, 10) };
    private readonly ListBox _list = new() { BorderThickness = new Thickness(0), Background = Brushes.Transparent };
    private readonly TextBlock _fullName = Text("停在一个目录上，完整名称会显示在这里。", 18, true);
    private readonly TextBlock _path = Text("", 11);
    private readonly TextBlock _listTitle = Text("当前圈的全部目录", 13, true);
    private readonly TextBlock _emptyList = Text("", 12);
    private readonly TextBlock _summary = Text("", 12);
    private readonly TextBlock _status = Text("点击轮盘只记录目标。示例文件：测试文档.txt", 12);
    private readonly TextBlock _source = Text("内置测试目录", 11);
    private readonly TextBlock _presetDescription = Text("", 12);
    private readonly Border _reader = new();
    private readonly ColumnDefinition _readerColumn = new() { Width = new GridLength(320) };
    private readonly Dictionary<string, Button> _presetButtons = [];
    private readonly DispatcherTimer _resize = new() { Interval = TimeSpan.FromMilliseconds(160) };
    private readonly RadialNameTip _nameTip;
    private IReadOnlyList<FolderMenuNode> _roots = [];
    private IReadOnlyList<FolderMenuNode>? _listedSource;
    private int _listedCount;
    private DirectoryTreeCache? _realCache;
    private string _rootPath = "示例目录";
    private RadialMenuSession? _session;
    private RadialLayoutPlan? _layout;
    private FolderMenuNode? _inspected;
    private string? _anchor;
    private Point? _customCenter;
    private int _readingRing;
    private int _generation;
    private int _sourceGeneration;
    private bool _updating;
    private bool _baseline;
    private bool _closed;

    public ReadabilityLabWindow()
    {
        Title = "灵环 · 目录阅读实验室";
        Width = Math.Min(1460, SystemParameters.WorkArea.Width - 40);
        Height = Math.Min(900, SystemParameters.WorkArea.Height - 40);
        MinWidth = Math.Min(1080, Width); MinHeight = Math.Min(680, Height);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = UiVisuals.Brush(UiVisuals.Canvas);
        FontFamily = UiVisuals.UiFont; FontSize = 13;
        Foreground = UiVisuals.Brush(UiVisuals.TextPrimary);
        UseLayoutRounding = true;
        Content = new Border { Background = Background, Child = _page };
        BuildPage();
        _nameTip = new RadialNameTip(_wheel);
        _wheel.NameTipRequested += (folder, point) =>
        {
            if (_companion.IsChecked != true) _nameTip.ShowName(folder,
                _rootPath, [], point);
        };
        _wheel.NameTipDismissRequested += () => _nameTip.IsOpen = false;
        _stage.Children.Add(_wheel);
        _wheel.StateChanged += RefreshReader;
        _wheel.Confirmed += path => RecordSelection(path);
        _wheel.CreateFolderRequested += _ => _status.Text = "本轮只比较目录浏览；点击目录可记录测试目标。";
        _wheel.DismissRequested += () => _status.Text = "在预览区域点击中键，可从该位置重新打开轮盘。";
        _stage.PreviewMouseDown += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Middle)
            {
                _customCenter = e.GetPosition(_stage); Rebuild(); e.Handled = true;
            }
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Back && e.OriginalSource is not TextBox) { Back(); e.Handled = true; }
        };
        _resize.Tick += (_, _) => { _resize.Stop(); Rebuild(); };
        _stage.SizeChanged += (_, _) => { _resize.Stop(); _resize.Start(); };
        foreach (var choice in new[] { _quantity, _direction, _width })
            choice.SelectionChanged += (_, _) => { if (!_updating) { MarkCustom(); Rebuild(); } };
        _position.SelectionChanged += (_, _) => { if (!_updating) { _customCenter = null; Rebuild(); } };
        _sample.SelectionChanged += (_, _) => { if (!_updating) { UseSamples(); Rebuild(false); } };
        _adapt.Click += (_, _) => { MarkCustom(); Rebuild(); };
        _companion.Click += (_, _) => { MarkCustom(false); _nameTip.IsOpen = false; SetReaderVisibility(); };
        _listRing.SelectionChanged += (_, _) => RefreshReader();
        _search.TextChanged += (_, _) => PopulateList();
        _list.SelectionChanged += (_, _) =>
        {
            if (!_updating && _list.SelectedItem is FolderMenuNode folder) Inspect(folder);
        };
        _list.MouseDoubleClick += async (_, _) => { if (_list.SelectedItem is FolderMenuNode folder) await EnterFromListAsync(folder); };
        _list.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter && _list.SelectedItem is FolderMenuNode folder)
            { e.Handled = true; await EnterFromListAsync(folder); }
        };
        Loaded += (_, _) => ApplyPreset("arc");
        Closed += (_, _) =>
        {
            _closed = true; ++_generation; ++_sourceGeneration; _resize.Stop(); _nameTip.IsOpen = false; _wheel.ReleaseSession(); _realCache?.Dispose();
        };
    }

    private void BuildPage()
    {
        _page.Margin = new Thickness(24, 20, 24, 16);
        _page.RowDefinitions.Add(new() { Height = GridLength.Auto });
        _page.RowDefinitions.Add(new() { Height = GridLength.Auto });
        _page.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        _page.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var badge = Text("独立 Demo · 点击仅作演示", 12); badge.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(badge, Dock.Right); header.Children.Add(badge);
        header.Children.Add(Text("目录阅读实验室", 26, true)); _page.Children.Add(header);
        var presets = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16) };
        foreach (var (key, label) in new[] { ("baseline", "原版对照"), ("dense", "六项横排"), ("arc", "沿弧排字"), ("adaptive", "自动阅读"), ("list", "完整名称列表") })
        {
            var button = UiVisuals.CreateButton(label, false, 110, 36); button.Margin = new Thickness(0, 0, 8, 0);
            button.Click += (_, _) => ApplyPreset(key); presets.Children.Add(button); _presetButtons[key] = button;
        }
        Grid.SetRow(presets, 1); _page.Children.Add(presets);
        var body = new Grid(); body.ColumnDefinitions.Add(new() { Width = new GridLength(220) });
        body.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); body.ColumnDefinitions.Add(_readerColumn);
        Grid.SetRow(body, 2); _page.Children.Add(body);
        var settings = new StackPanel { Margin = new Thickness(0, 0, 18, 0) };
        settings.Children.Add(Text("调整你的阅读方式", 16, true));
        _presetDescription.Margin = new Thickness(0, 10, 0, 20); settings.Children.Add(_presetDescription);
        AddChoice(settings, "每圈最多显示", _quantity); AddChoice(settings, "目录文字", _direction); AddChoice(settings, "圆环空间", _width);
        settings.Children.Add(_adapt); settings.Children.Add(_companion);
        AddChoice(settings, "测试内容", _sample); AddChoice(settings, "打开位置", _position);
        var browse = UiVisuals.CreateButton("读取我的目录…", false, 160); browse.Margin = new Thickness(0, 4, 0, 8);
        browse.Click += async (_, _) => await ChooseFolderAsync(); settings.Children.Add(browse);
        _source.Margin = new Thickness(0, 0, 0, 16); settings.Children.Add(_source);
        var back = UiVisuals.CreateButton("返回上一级", false, 160); back.Click += (_, _) => Back(); settings.Children.Add(back);
        var reset = UiVisuals.CreateButton("重置展开层级", false, 160); reset.Margin = new Thickness(0, 8, 0, 0);
        reset.Click += (_, _) => { _anchor = null; Rebuild(false); }; settings.Children.Add(reset);
        var scrollSettings = new ScrollViewer { Content = settings, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        body.Children.Add(scrollSettings);
        var preview = new Grid { Margin = new Thickness(0, 0, 16, 0) };
        preview.RowDefinitions.Add(new() { Height = GridLength.Auto }); preview.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        preview.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var hint = Text("在预览区点中键，测试任意位置；滚轮翻页，右键返回。", 12); hint.Margin = new Thickness(12, 0, 0, 10); preview.Children.Add(hint);
        Grid.SetRow(_stage, 1); preview.Children.Add(_stage);
        _summary.Margin = new Thickness(12, 10, 0, 8); _summary.Height = 44; _summary.TextTrimming = TextTrimming.CharacterEllipsis;
        Grid.SetRow(_summary, 2); preview.Children.Add(_summary);
        Grid.SetColumn(preview, 1); body.Children.Add(preview);
        var readerContent = new Grid { Margin = new Thickness(16) };
        readerContent.RowDefinitions.Add(new() { Height = GridLength.Auto }); readerContent.RowDefinitions.Add(new() { Height = GridLength.Auto });
        readerContent.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) }); readerContent.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var details = new StackPanel(); details.Children.Add(Text("完整名称", 13, true));
        _fullName.Margin = new Thickness(0, 8, 0, 8); details.Children.Add(_fullName);
        _path.Margin = new Thickness(0, 0, 0, 16); _path.MaxHeight = 36; _path.TextTrimming = TextTrimming.CharacterEllipsis;
        details.Children.Add(_path); readerContent.Children.Add(details);
        var search = new StackPanel(); search.Children.Add(_listRing); _listTitle.Margin = new Thickness(0, 12, 0, 0); search.Children.Add(_listTitle);
        _search.ToolTip = "输入名称片段，多个关键词用空格分隔";
        search.Children.Add(Text("搜索这一圈", 11)); search.Children.Add(_search); Grid.SetRow(search, 1); readerContent.Children.Add(search);
        var name = new FrameworkElementFactory(typeof(TextBlock)); name.SetBinding(TextBlock.TextProperty, new Binding(nameof(FolderMenuNode.DisplayName)));
        name.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap); name.SetValue(TextBlock.FontSizeProperty, 13d);
        name.SetValue(TextBlock.LineHeightProperty, 21d); name.SetValue(FrameworkElement.MarginProperty, new Thickness(3, 6, 3, 6));
        _list.ItemTemplate = new DataTemplate { VisualTree = name };
        var itemStyle = new Style(typeof(ListBoxItem)); itemStyle.Setters.Add(new Setter(HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        itemStyle.Setters.Add(new Setter(PaddingProperty, new Thickness(6, 2, 6, 2))); _list.ItemContainerStyle = itemStyle;
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
        Grid.SetRow(_list, 2); readerContent.Children.Add(_list);
        _emptyList.Margin = new Thickness(4, 12, 4, 0); _emptyList.IsHitTestVisible = false;
        Grid.SetRow(_emptyList, 2); readerContent.Children.Add(_emptyList);
        var actions = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        actions.Children.Add(Text("单击查看全名 · 双击进入目录", 11));
        var confirm = UiVisuals.CreateButton("记录这个目标", true, 180); confirm.Margin = new Thickness(0, 10, 0, 0);
        confirm.Click += (_, _) => { if (_inspected is not null) RecordSelection(_inspected.RelativePath); };
        actions.Children.Add(confirm); Grid.SetRow(actions, 3); readerContent.Children.Add(actions);
        _reader.Background = UiVisuals.Brush(UiVisuals.Surface); _reader.CornerRadius = new CornerRadius(14);
        _reader.BorderBrush = UiVisuals.Brush(UiVisuals.BorderColor); _reader.BorderThickness = new Thickness(1);
        _reader.Child = readerContent; Grid.SetColumn(_reader, 2); body.Children.Add(_reader);
        _status.Margin = new Thickness(0, 16, 0, 0); _status.MaxHeight = 42; _status.TextTrimming = TextTrimming.CharacterEllipsis;
        Grid.SetRow(_status, 3); _page.Children.Add(_status);
    }

    private static ComboBox Choice(params string[] items) => new() { ItemsSource = items, SelectedIndex = 0, Height = 34, Padding = new Thickness(6, 3, 6, 3) };
    private static TextBlock Text(string text, double size, bool bold = false) => new()
    {
        Text = text, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        TextWrapping = TextWrapping.Wrap, Foreground = UiVisuals.Brush(size <= 12 ? UiVisuals.TextSecondary : UiVisuals.TextPrimary),
        LineHeight = size * 1.55
    };
    private static void AddChoice(Panel parent, string label, ComboBox choice)
    {
        var title = Text(label, 12, true); title.Margin = new Thickness(0, 0, 0, 6);
        parent.Children.Add(title); choice.Margin = new Thickness(0, 0, 0, 14); parent.Children.Add(choice);
    }

    internal void ApplyPreset(string preset)
    {
        _updating = true;
        _baseline = preset == "baseline";
        _quantity.SelectedIndex = _baseline ? 2 : 4;
        _direction.SelectedIndex = preset == "arc" ? 1 : preset == "adaptive" ? 2 : 0;
        _width.SelectedIndex = _baseline ? 0 : 1;
        _adapt.IsChecked = !_baseline; _companion.IsChecked = preset is "adaptive" or "list";
        foreach (var (key, button) in _presetButtons)
        {
            button.Background = UiVisuals.Brush(key == preset ? UiVisuals.Accent : UiVisuals.SurfaceMuted);
            button.Foreground = key == preset ? Brushes.White : UiVisuals.Brush(UiVisuals.TextPrimary);
        }
        _presetDescription.Text = preset switch
        {
            "baseline" => "当前四项轮盘，作为尺寸和文字容量的基线。",
            "dense" => "多看两项，并给长名称更宽的圆环和最多四行空间。",
            "arc" => "字与字顺着弧线首尾相接，从上往下读；换列从左往右。英文和数字成段侧排。",
            "list" => "轮盘快速定位，侧栏阅读全部名称，也可搜索和进入。",
            _ => "按每个扇区的实际容量选横排或弧形排字；显示量相同优先横排。"
        };
        _updating = false;
        if (_roots.Count == 0 && _realCache is null) UseSamples();
        _anchor = null; _customCenter = null; SetReaderVisibility(); Rebuild(false);
    }

    private void MarkCustom(bool enhanced = true)
    {
        if (enhanced) _baseline = false;
        foreach (var button in _presetButtons.Values)
        {
            button.Background = UiVisuals.Brush(UiVisuals.SurfaceMuted);
            button.Foreground = UiVisuals.Brush(UiVisuals.TextPrimary);
        }
        _presetDescription.Text = "自定义组合。调整数量、文字方向和圆环空间，也可随时切换上方的对照方案。";
    }

    private RadialDisplayOptions Options => new(Math.Clamp(_quantity.SelectedIndex + 2, 2, 8),
        (RadialLabelDirection)Math.Max(0, _direction.SelectedIndex), new[] { 60d, 82, 104 }[Math.Max(0, _width.SelectedIndex)],
        _adapt.IsChecked == true, !_baseline);

    private void SetReaderVisibility()
    {
        _reader.Visibility = _companion.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        _readerColumn.Width = new GridLength(_companion.IsChecked == true ? 320 : 0);
    }

    private async void Rebuild(bool preserve = true)
    {
        if (!IsLoaded || _closed || _stage.ActualWidth < 100 || _stage.ActualHeight < 100) return;
        var generation = ++_generation;
        var previousPath = preserve ? _session?.CurrentDestination?.RelativePath : null;
        var options = Options;
        var area = new RadialRect(0, 0, _stage.ActualWidth, _stage.ActualHeight);
        var point = _customCenter ?? (_position.SelectedIndex switch
        {
            1 => new Point(50, area.Height - 65), 2 => new Point(area.Width - 50, 65),
            _ => new Point(area.Width / 2, area.Height / 2)
        });
        var layout = RadialLayout.Create(new RadialPoint(Math.Clamp(point.X, 20, area.Width - 20),
            Math.Clamp(point.Y, 20, area.Height - 20)), area, ringWidth: options.RingWidth);
        var next = new RadialMenuSession(_roots, options.Capacities(layout));
        if (!string.IsNullOrEmpty(_anchor)) await next.SelectStartPathAsync(_anchor);
        if (!string.IsNullOrEmpty(previousPath)) await next.SelectPathAsync(previousPath, _anchor);
        else if (_anchor is null && _realCache is null) next.PrimeForDemo();
        if (_closed || generation != _generation) { next.Dispose(); return; }
        _session = next; _layout = layout; _listedSource = null;
        _wheel.Width = layout.WindowBounds.Width; _wheel.Height = layout.WindowBounds.Height;
        Canvas.SetLeft(_wheel, layout.WindowBounds.X); Canvas.SetTop(_wheel, layout.WindowBounds.Y);
        _wheel.DisplayOptions = options;
        _wheel.Configure(layout, next, 1); _wheel.PlayOpen(); RefreshReader();
    }

    private void RefreshReader()
    {
        if (_updating || _session is null || _layout is null || _closed) return;
        _anchor = _session.VisibleAnchorPath;
        if (_wheel.ReadingHit.RingIndex >= 0)
        {
            _readingRing = _wheel.ReadingHit.RingIndex;
            var folder = _session.GetFolder(_readingRing, _wheel.ReadingHit.SlotIndex);
            if (folder is not null && !ReferenceEquals(_inspected, folder)) Inspect(folder);
        }
        var ring = _listRing.SelectedIndex > 0 ? _listRing.SelectedIndex - 1 : _readingRing;
        while (ring > 0 && _session.GetItemsForRing(ring).Count == 0) ring--;
        var items = _session.GetItemsForRing(ring);
        if (!ReferenceEquals(items, _listedSource) || items.Count != _listedCount)
        { _listedSource = items; _listedCount = items.Count; PopulateList(); }
        _listTitle.Text = $"第 {ring + 1} 圈 · 共 {items.Count} 项";
        var labels = _wheel.MeasureVisibleLabels();
        _summary.Text = $"每圈上限 {Options.MaximumItems} 项 · 当前容量 {string.Join(" / ", Enumerable.Range(0, 3).Select(_session.GetCapacity))}\n" +
            $"可见 {labels.Count} 项 · 完整标签 {labels.Count(label => !label.IsTruncated)} 项 · 显示字符 {labels.Sum(label => label.VisibleCharacters)}/{labels.Sum(label => label.TotalCharacters)}";
    }

    private void Inspect(FolderMenuNode folder)
    {
        _inspected = folder; _fullName.Text = folder.DisplayName;
        _path.Text = folder.RelativePath.Replace("\\", " › ");
        _path.ToolTip = _path.Text;
        _updating = true;
        if (_list.Items.Contains(folder)) _list.SelectedItem = folder;
        _updating = false;
    }

    private void PopulateList()
    {
        var query = _search.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        _updating = true;
        _list.ItemsSource = (_listedSource ?? []).Where(node => query.All(term => node.DisplayName.Contains(term, StringComparison.CurrentCultureIgnoreCase))).ToArray();
        _emptyList.Text = _list.Items.Count > 0 ? "" : query.Length > 0 ? "没有匹配的目录，试试更短的关键词。" : "这一圈没有可显示的下级目录。";
        if (_inspected is not null && _list.Items.Contains(_inspected)) _list.SelectedItem = _inspected;
        _updating = false;
    }

    private async Task EnterFromListAsync(FolderMenuNode folder)
    {
        var generation = _generation;
        if (folder.Children is AsyncFolderMenuNodeList lazy) await lazy.EnsureLoadedAsync();
        if (_closed || generation != _generation) return;
        if (folder.Children.Count == 0) { _status.Text = $"“{folder.DisplayName}”没有可展开的下级目录。"; return; }
        _anchor = folder.RelativePath; Rebuild(false); _search.Clear();
    }

    private void RecordSelection(string path)
    {
        _status.Text = $"已记录演示目标：{path.Replace("\\", " › ")}　（文件保持原位）";
        _status.ToolTip = _status.Text;
        UiMotion.Reveal(_status, 3, 160);
    }

    private void Back()
    {
        if (_wheel.NavigateBack()) { _anchor = _session?.VisibleAnchorPath; RefreshReader(); }
        else _status.Text = "已回到这组测试目录的起点。";
    }

    private void UseSamples()
    {
        ++_sourceGeneration;
        _realCache?.Dispose(); _realCache = null; _anchor = null;
        _rootPath = "示例目录";
        _source.Text = "内置测试目录 · 不创建实际文件夹";
        _source.ToolTip = null;
        _inspected = null; _fullName.Text = "停在一个目录上，完整名称会显示在这里。"; _path.Text = "";
        _search.Clear();
        _roots = CreateSamples(_sample.SelectedIndex);
    }

    private async Task ChooseFolderAsync()
    {
        var picker = new Microsoft.Win32.OpenFolderDialog { Title = "选择要浏览的目录（只读演示）" };
        if (picker.ShowDialog(this) != true) return;
        await LoadFolderAsync(picker.FolderName);
    }

    internal async Task<bool> LoadFolderAsync(string path)
    {
        var sourceGeneration = ++_sourceGeneration;
        _source.Text = "正在读取目录…";
        DirectoryTreeCache? pendingCache = null;
        try
        {
            var profile = ArchiveScope.CreateNavigationProfile(path);
            pendingCache = new DirectoryTreeCache(profile.RootPath);
            await pendingCache.ReloadAsync();
            await pendingCache.WarmStartPathAsync(profile.StartPath);
            if (_closed || sourceGeneration != _sourceGeneration) return false;
            var snapshot = pendingCache.CreateMenuSnapshot() ?? throw new IOException("目录尚未读取完成，请重试。");
            _realCache?.Dispose(); _realCache = pendingCache; pendingCache = null;
            _roots = snapshot.Folders; _anchor = profile.StartPath; _rootPath = profile.RootPath;
            _source.Text = path; _source.ToolTip = path;
            _inspected = null; _fullName.Text = "停在一个目录上，完整名称会显示在这里。"; _path.Text = "";
            _search.Clear();
            Rebuild(false);
            return true;
        }
        catch (Exception exception)
        {
            if (!_closed && sourceGeneration == _sourceGeneration) _source.Text = "读取失败：" + exception.Message;
            return false;
        }
        finally { pendingCache?.Dispose(); }
    }

    internal static IReadOnlyList<FolderMenuNode> CreateSamples(int scenario)
    {
        string[] names = scenario switch
        {
            1 => ["Project_Atlas_2026_Final_Approved", "Project_Atlas_2026_Final_Revision02", "2026年Q4品牌推广_Approved", "客户反馈_v12_最终确认", "DesignSystem_组件与设计规范", "Research_用户访谈与分析", "合同Contract_2026签署版", "Asset_Library_源文件与导出", "年度报告_Report_2026", "Delivery_交付清单与验收", "Operations_日常运营与复盘", "Archive_旧版本留存"],
            2 => ["项目", "合同", "设计", "财务", "素材", "报告", "会议", "参考", "归档", "运营", "交付", "研究"],
            _ => ["2026年度项目资料与合同归档_一季度", "2026年度项目资料与合同归档_二季度", "2026年度项目资料与合同归档_三季度", "2026年度项目资料与合同归档_四季度", "客户方案与多轮修改记录_最终确认版", "品牌视觉设计规范及源文件_正式发布", "市场活动策划与执行物料_线下活动", "市场活动策划与执行物料_线上活动", "产品需求与用户研究_访谈整理", "采购合同与供应商资料_已签署", "年度经营复盘与预算规划_下一年度", "历史版本与参考资料_保留备查"]
        };
        var childNames = new[] { "客户沟通与需求确认_已签字", "客户沟通与需求确认_待补充", "设计方案与完整源文件_最终版", "设计方案与完整源文件_修订版", "会议纪要与行动事项_本周", "会议纪要与行动事项_下周", "预算清单与付款凭证_已结算", "预算清单与付款凭证_待结算", "项目交付清单与验收记录", "参考案例与调研材料", "测试材料_AI_2026_v2", "空文件夹示例" };
        var finalNames = new[] { "市场推广方案与产品介绍_正式发布", "市场推广方案与产品介绍_内部评审", "客户反馈与修改记录_第一轮", "客户反馈与修改记录_第二轮", "主视觉设计与宣传物料_印刷版", "主视觉设计与宣传物料_屏幕版", "数据明细与分析报告_已核对", "数据明细与分析报告_待核对", "视频脚本与剪辑素材_终审", "交付清单与验收凭证_完整", "AI_Design_2026_V2_Final", "空目录_无需展开" };
        IReadOnlyList<FolderMenuNode> Build(string parent, int depth) => (depth == 0 ? names : scenario == 2 ? names : depth == 1 ? childNames : finalNames)
            .Select((name, index) =>
            {
                var path = string.IsNullOrEmpty(parent) ? name : Path.Combine(parent, name);
                return new FolderMenuNode(name, path, depth < 3 && index != 11 ? Build(path, depth + 1) : []);
            }).ToArray();
        return Build("", 0);
    }

    internal async Task CaptureComparisonsAsync(string directory, string? fixture = null)
    {
        Directory.CreateDirectory(directory);
        _wheel.IsHitTestVisible = false;
        var records = new List<object>();
        var checks = new List<string>();
        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException(name);
            checks.Add(name);
        }
        async Task Settle()
        {
            await Task.Delay(650);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        }
        void Capture(string name) => UiVisuals.SaveSnapshot((FrameworkElement)Content, Path.Combine(directory, name + ".png"));
        foreach (var preset in new[] { "baseline", "dense", "arc", "adaptive", "list" })
        {
            ApplyPreset(preset); await Settle();
            _readingRing = 2; Inspect(_session!.GetFolder(2, 0)!); RefreshReader();
            Capture(preset);
            records.Add(new { preset, options = Options, capacities = Enumerable.Range(0, 3).Select(index => _session!.GetCapacity(index)).ToArray(), labels = _wheel.MeasureVisibleLabels() });
        }

        ApplyPreset("arc"); _adapt.IsChecked = false; _quantity.SelectedIndex = 6;
        await Settle();
        Check(Enumerable.Range(0, 3).All(ring => _session!.GetCapacity(ring) == 8), "Eight items in all three rings");
        _wheel.HoverDemo(2);
        for (var i = 0; i < 3; i++) Check(_wheel.RotateDemo(1), $"Queued wheel step {i + 1}");
        await Task.Delay(90); Capture("arc-eight-scrolling");
        await Task.Delay(1000);
        Check(!_wheel.IsTransitioning && _session!.GetRingView(2).Offset == 3, "Rapid scrolling settles on the requested offset");
        var band = _layout!.Rings[2];
        var angle = RadialLayout.SlotStart(band, 8) + 7.5 * RadialLayout.SlotSweep(band, 8);
        string? recorded = null;
        void Record(string path) => recorded = path;
        _wheel.Confirmed += Record;
        try { _wheel.ProcessPrimaryClick(RadialGeometry.Polar(_layout.LocalCenter, (band.InnerRadius + band.OuterRadius) / 2, angle)); }
        finally { _wheel.Confirmed -= Record; }
        Check(recorded == _session!.GetFolder(2, 7)!.RelativePath, "Eighth slot confirms its actual directory after scrolling");
        Capture("arc-eight-settled");

        _listRing.SelectedIndex = 3; _search.Text = "正式 发布";
        Check(_list.Items.Count == 1, "Companion search accepts multiple keywords");
        var target = (FolderMenuNode)_list.Items[0];
        await EnterFromListAsync(target); await Settle();
        Check(_session!.VisibleAnchorPath == target.RelativePath, "Companion enters the selected directory");
        Back(); await Settle();
        Check(_session!.VisibleAnchorPath != target.RelativePath, "Back returns from the companion directory");
        _search.Text = "不存在的测试关键词";
        Check(_list.Items.Count == 0 && _emptyList.Text.Length > 0, "Empty search has visible feedback");
        _search.Clear(); _listRing.SelectedIndex = 0;

        ApplyPreset("arc"); _quantity.SelectedIndex = 6; _position.SelectedIndex = 1;
        await Settle(); Capture("corner-eight-limit");
        Check(Enumerable.Range(0, 3).Any(ring => _session!.GetCapacity(ring) < 8), "Corner density reduces the inner capacity");
        _position.SelectedIndex = 0; _sample.SelectedIndex = 1;
        await Settle(); Capture("arc-mixed-names");
        _sample.SelectedIndex = 2; await Settle(); Capture("arc-short-names");
        _sample.SelectedIndex = 0; ApplyPreset("adaptive"); Width = MinWidth; Height = MinHeight;
        await Settle(); Capture("minimum-window");
        if (fixture is not null)
        {
            Check(await LoadFolderAsync(fixture), "Read-only filesystem fixture loads"); await Settle();
            Check(_session!.GetItemsForRing(0).Select(node => node.DisplayName).Order().SequenceEqual(new[] { "Probe_A", "Probe_B" }),
                "Selected filesystem directory shows its own children");
            var empty = _session.GetItemsForRing(0).Single(node => node.DisplayName == "Probe_B");
            await EnterFromListAsync(empty);
            Check(_status.Text.Contains("没有可展开"), "Filesystem empty folder reports its state");
        }
        File.WriteAllText(Path.Combine(directory, "comparison.json"), JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.Combine(directory, "interaction-checks.json"), JsonSerializer.Serialize(checks, new JsonSerializerOptions { WriteIndented = true }));
    }
}
