using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using GongSiOrganizer.Core;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using Image = System.Windows.Controls.Image;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Point = System.Windows.Point;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfOrientation = System.Windows.Controls.Orientation;

namespace GongSiOrganizer.RadialPrototype.Presentation;

internal sealed record OnboardingRootSelectionResult(
    bool Applied,
    bool Cancelled,
    string RootPath,
    string? Message);

internal sealed class OnboardingWindow : Window
{
    private string _rootPath;
    private readonly Func<string, Task<OnboardingRootSelectionResult>>? _chooseArchiveRoot;
    private readonly bool _rootSelectionRequired;
    private readonly Border _surface;
    private readonly Grid _pageHost;
    private readonly TextBlock _stepLabel;
    private readonly TextBlock _title;
    private readonly TextBlock _subtitle;
    private readonly StackPanel _bulletPanel;
    private readonly Border _rootSetupCard;
    private readonly TextBlock _rootSetupPath;
    private readonly TextBlock _rootSetupStatus;
    private readonly Button _chooseRootButton;
    private readonly Grid _illustrationHost;
    private readonly StackPanel _dots;
    private readonly Button _skipButton;
    private readonly Button _backButton;
    private readonly Button _nextButton;
    private Button _closeButton = null!;
    private int _pageIndex;
    private bool _rootSelectionInProgress;
    private bool _hasValidArchiveRoot;

    private static readonly GuidePage[] Pages =
    [
        new(
            "从鼠标所在位置开始归档",
            "不用先打开软件，也不用拖着文件寻找窗口。",
            ["在桌面或资源管理器中选中文件", "按一下鼠标中键，轮盘就在当前位置出现"],
            GuideIllustration.Wheel),
        new(
            "移动鼠标，逐层找到目录",
            "轮盘会沿着你的选择自然展开，目录多时直接滚动。",
            ["悬停某一扇区，展开它的下一级", "滚轮切换同级目录；右键返回上一级"],
            GuideIllustration.Navigation),
        new(
            "确认移动，也能临时建目录",
            "所有文件操作都围绕当前悬停的目标完成。",
            ["左键确认并直接移动，不额外复制", "Ctrl + 左键在当前目标下新建文件夹", "点击轮盘外空白或按 Esc 取消"],
            GuideIllustration.Actions),
        new(
            "选好轮盘起点，马上开始",
            "“此电脑”是最高层，也可以从常用文件夹直接展开。",
            ["最高层可在不同本地磁盘间归档", "向下选择的文件夹只是默认起点", "右键可逐级返回磁盘和“此电脑”"],
            GuideIllustration.Tray)
    ];

    public OnboardingWindow(
        string rootPath,
        Func<string, Task<OnboardingRootSelectionResult>>? chooseArchiveRoot = null,
        bool rootSelectionRequired = false)
    {
        _rootPath = rootPath;
        _chooseArchiveRoot = chooseArchiveRoot;
        _rootSelectionRequired = rootSelectionRequired;
        _hasValidArchiveRoot = ArchiveScope.IsAvailable(rootPath);
        Title = "文件快速归档 · 新手引导";
        Width = 760;
        Height = 520;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = true;
        Topmost = true;
        FontFamily = UiVisuals.UiFont;

        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(62) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(68) });

        var header = CreateHeader();
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        _pageHost = new Grid { Margin = new Thickness(24, 6, 24, 0) };
        _pageHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.98, GridUnitType.Star) });
        _pageHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.02, GridUnitType.Star) });
        var textColumn = new StackPanel
        {
            Margin = new Thickness(8, 22, 22, 0),
            VerticalAlignment = VerticalAlignment.Top
        };
        _stepLabel = new TextBlock
        {
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = UiVisuals.Brush(UiVisuals.Accent)
        };
        textColumn.Children.Add(_stepLabel);
        _title = new TextBlock
        {
            Margin = new Thickness(0, 9, 0, 0),
            FontSize = 23,
            FontWeight = FontWeights.SemiBold,
            Foreground = UiVisuals.Brush(UiVisuals.TextPrimary),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 32
        };
        textColumn.Children.Add(_title);
        _subtitle = new TextBlock
        {
            Margin = new Thickness(0, 10, 0, 0),
            FontSize = 11.5,
            Foreground = UiVisuals.Brush(UiVisuals.TextSecondary),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 19
        };
        textColumn.Children.Add(_subtitle);
        _bulletPanel = new StackPanel { Margin = new Thickness(0, 19, 0, 0) };
        textColumn.Children.Add(_bulletPanel);

        _rootSetupPath = new TextBlock
        {
            Text = ArchiveScope.GetDisplayName(_rootPath),
            Margin = new Thickness(0, 4, 8, 0),
            FontSize = 10.2,
            FontWeight = FontWeights.SemiBold,
            Foreground = UiVisuals.Brush(UiVisuals.TextPrimary),
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = ArchiveScope.GetDisplayName(_rootPath)
        };
        _rootSetupStatus = new TextBlock
        {
            Text = _rootSelectionRequired && !_hasValidArchiveRoot
                ? "请先选择轮盘起点，设置成功后才能开始"
                : "层级关系：此电脑 › 本地磁盘 › 文件夹",
            Margin = new Thickness(0, 5, 0, 0),
            FontSize = 8.8,
            Foreground = UiVisuals.Brush(UiVisuals.TextSecondary),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        _chooseRootButton = UiVisuals.CreateButton("选择起点", false, 70, 28);
        _chooseRootButton.Padding = new Thickness(8, 2, 8, 2);
        _chooseRootButton.FontSize = 9.5;
        _chooseRootButton.IsEnabled = _chooseArchiveRoot is not null;
        _chooseRootButton.Click += OnChooseRootClick;

        var rootSetupGrid = new Grid();
        rootSetupGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        rootSetupGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        rootSetupGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rootSetupGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rootSetupGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var rootSetupLabel = new TextBlock
        {
            Text = "轮盘默认起点",
            FontSize = 9.3,
            Foreground = UiVisuals.Brush(UiVisuals.TextSecondary)
        };
        rootSetupGrid.Children.Add(rootSetupLabel);
        Grid.SetRow(_rootSetupPath, 1);
        rootSetupGrid.Children.Add(_rootSetupPath);
        Grid.SetColumn(_chooseRootButton, 1);
        Grid.SetRowSpan(_chooseRootButton, 2);
        _chooseRootButton.VerticalAlignment = VerticalAlignment.Center;
        rootSetupGrid.Children.Add(_chooseRootButton);
        Grid.SetRow(_rootSetupStatus, 2);
        Grid.SetColumnSpan(_rootSetupStatus, 2);
        rootSetupGrid.Children.Add(_rootSetupStatus);
        _rootSetupCard = new Border
        {
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 7, 0, 0),
            Padding = new Thickness(11, 9, 10, 8),
            CornerRadius = new CornerRadius(11),
            Background = UiVisuals.Brush(Color.FromRgb(239, 246, 254)),
            BorderBrush = UiVisuals.Brush(Color.FromRgb(201, 222, 246)),
            BorderThickness = new Thickness(0.8),
            Child = rootSetupGrid
        };
        textColumn.Children.Add(_rootSetupCard);
        Grid.SetColumn(textColumn, 0);
        _pageHost.Children.Add(textColumn);

        _illustrationHost = new Grid
        {
            Margin = new Thickness(10, 5, 5, 2),
            ClipToBounds = true
        };
        Grid.SetColumn(_illustrationHost, 1);
        _pageHost.Children.Add(_illustrationHost);
        Grid.SetRow(_pageHost, 1);
        root.Children.Add(_pageHost);

        var footer = new Grid { Margin = new Thickness(31, 7, 31, 14) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _dots = new StackPanel
        {
            Orientation = WpfOrientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(_dots, 0);
        footer.Children.Add(_dots);

        var actions = new StackPanel
        {
            Orientation = WpfOrientation.Horizontal,
            HorizontalAlignment = WpfHorizontalAlignment.Right
        };
        _skipButton = UiVisuals.CreateButton(
            _rootSelectionRequired ? "设置轮盘起点" : "跳过引导",
            false,
            _rootSelectionRequired ? 102 : 82,
            36);
        _skipButton.Background = Brushes.Transparent;
        _skipButton.BorderThickness = new Thickness(0);
        _skipButton.Click += (_, _) => Complete();
        actions.Children.Add(_skipButton);
        _backButton = UiVisuals.CreateButton("上一步", false, 78, 36);
        _backButton.Margin = new Thickness(8, 0, 0, 0);
        _backButton.Click += (_, _) => ChangePage(-1);
        actions.Children.Add(_backButton);
        _nextButton = UiVisuals.CreateButton("下一步", true, 88, 36);
        _nextButton.Margin = new Thickness(9, 0, 0, 0);
        _nextButton.Click += (_, _) =>
        {
            if (_pageIndex == Pages.Length - 1)
                Complete();
            else
                ChangePage(1);
        };
        actions.Children.Add(_nextButton);
        Grid.SetColumn(actions, 2);
        footer.Children.Add(actions);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        _surface = new Border
        {
            CornerRadius = new CornerRadius(22),
            Background = UiVisuals.Brush(Color.FromArgb(253, 248, 250, 253)),
            BorderBrush = UiVisuals.Brush(Color.FromArgb(190, 191, 207, 225)),
            BorderThickness = new Thickness(0.8),
            Effect = new DropShadowEffect
            {
                BlurRadius = 30,
                ShadowDepth = 8,
                Opacity = 0.25,
                Color = Colors.Black
            },
            Child = root
        };
        Content = _surface;
        PreviewKeyDown += OnPreviewKeyDown;
        Loaded += (_, _) =>
        {
            RenderPage(false);
            PlayWindowOpen();
        };
        Closing += (_, args) =>
        {
            if (!_rootSelectionInProgress) return;
            args.Cancel = true;
            _rootSetupStatus.Text = "正在读取目录，请稍候";
            _rootSetupStatus.Foreground = UiVisuals.Brush(UiVisuals.TextSecondary);
        };
    }

    public bool Completed { get; private set; }

    public void SaveSnapshot(string path) => UiVisuals.SaveSnapshot(_surface, path);

    public void ShowPageForPreview(int pageIndex)
    {
        _pageIndex = Math.Clamp(pageIndex, 0, Pages.Length - 1);
        RenderPage(false);
    }

    private Grid CreateHeader()
    {
        var header = new Grid { Margin = new Thickness(26, 18, 20, 0) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = new Image
        {
            Source = UiVisuals.LoadAppIcon(),
            Width = 32,
            Height = 32,
            Stretch = Stretch.Uniform
        };
        header.Children.Add(icon);
        var brand = new StackPanel
        {
            Margin = new Thickness(7, -1, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        brand.Children.Add(new TextBlock
        {
            Text = "文件快速归档",
            FontSize = 13.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = UiVisuals.Brush(UiVisuals.TextPrimary)
        });
        brand.Children.Add(new TextBlock
        {
            Text = "新手引导",
            Margin = new Thickness(0, 1, 0, 0),
            FontSize = 9.5,
            Foreground = UiVisuals.Brush(UiVisuals.TextSecondary)
        });
        Grid.SetColumn(brand, 1);
        header.Children.Add(brand);

        _closeButton = UiVisuals.CreateButton("×", false, 32, 32);
        _closeButton.Width = 32;
        _closeButton.Padding = new Thickness(0);
        _closeButton.FontFamily = new FontFamily("Segoe UI");
        _closeButton.FontSize = 17;
        _closeButton.Background = Brushes.Transparent;
        _closeButton.BorderThickness = new Thickness(0);
        _closeButton.Click += (_, _) => Close();
        Grid.SetColumn(_closeButton, 2);
        header.Children.Add(_closeButton);
        return header;
    }

    private void ChangePage(int direction)
    {
        if (_rootSelectionInProgress) return;
        var next = Math.Clamp(_pageIndex + direction, 0, Pages.Length - 1);
        if (next == _pageIndex) return;
        _pageIndex = next;
        RenderPage(true, direction);
    }

    private void RenderPage(bool animate, int direction = 1)
    {
        var page = Pages[_pageIndex];
        _stepLabel.Text = $"第 {_pageIndex + 1} 步，共 {Pages.Length} 步";
        _title.Text = page.Title;
        _subtitle.Text = page.Subtitle;
        _bulletPanel.Children.Clear();
        foreach (var bullet in page.Bullets)
            _bulletPanel.Children.Add(CreateBullet(bullet));
        _rootSetupCard.Visibility = _pageIndex == Pages.Length - 1
            ? Visibility.Visible
            : Visibility.Collapsed;
        _illustrationHost.Children.Clear();
        _illustrationHost.Children.Add(CreateIllustration(page.Illustration));
        _backButton.Visibility = _pageIndex == 0 ? Visibility.Collapsed : Visibility.Visible;
        _nextButton.Content = _pageIndex == Pages.Length - 1 ? "开始使用" : "下一步";
        UpdateNavigationAvailability();
        RenderDots();

        if (!animate) return;
        var translate = new TranslateTransform(direction > 0 ? 18 : -18, 0);
        _pageHost.RenderTransform = translate;
        _pageHost.Opacity = 0;
        _pageHost.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(170))
            {
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut }
            });
        translate.BeginAnimation(
            TranslateTransform.XProperty,
            new DoubleAnimation(translate.X, 0, TimeSpan.FromMilliseconds(210))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
    }

    private async void OnChooseRootClick(object sender, RoutedEventArgs e)
    {
        if (_chooseArchiveRoot is null || !_chooseRootButton.IsEnabled) return;

        SetRootSelectionBusy(true);
        _rootSetupStatus.Text = "请选择轮盘起点，读取完成前不会改变当前设置";
        _rootSetupStatus.Foreground = UiVisuals.Brush(UiVisuals.TextSecondary);
        var wasTopmost = Topmost;
        Topmost = false;
        try
        {
            var result = await _chooseArchiveRoot(_rootPath);
            if (result.Cancelled)
            {
                _rootSetupStatus.Text = _rootSelectionRequired && !_hasValidArchiveRoot
                    ? "尚未设置轮盘起点，请重新选择"
                    : "未做更改，继续使用当前目录";
                _rootSetupStatus.Foreground = UiVisuals.Brush(UiVisuals.TextSecondary);
                return;
            }

            if (!result.Applied)
            {
                _rootSetupStatus.Text = string.IsNullOrWhiteSpace(result.Message)
                    ? "无法读取该目录，已保留原来的设置"
                    : result.Message;
                _rootSetupStatus.Foreground = UiVisuals.Brush(Color.FromRgb(190, 70, 66));
                return;
            }

            _rootPath = result.RootPath;
            _hasValidArchiveRoot = true;
            _rootSetupPath.Text = ArchiveScope.GetDisplayName(_rootPath);
            _rootSetupPath.ToolTip = ArchiveScope.GetDisplayName(_rootPath);
            _rootSetupStatus.Text = string.IsNullOrWhiteSpace(result.Message)
                ? "轮盘起点设置成功"
                : result.Message;
            _rootSetupStatus.Foreground = UiVisuals.Brush(Color.FromRgb(45, 139, 94));
            _illustrationHost.Children.Clear();
            _illustrationHost.Children.Add(CreateTrayIllustration());
        }
        catch (Exception exception)
        {
            _rootSetupStatus.Text = $"设置失败：{exception.Message}";
            _rootSetupStatus.Foreground = UiVisuals.Brush(Color.FromRgb(190, 70, 66));
        }
        finally
        {
            Topmost = wasTopmost;
            Activate();
            SetRootSelectionBusy(false);
        }
    }

    private void SetRootSelectionBusy(bool busy)
    {
        _rootSelectionInProgress = busy;
        _chooseRootButton.Content = busy ? "正在读取…" : "选择起点";
        _chooseRootButton.IsEnabled = !busy && _chooseArchiveRoot is not null;
        _skipButton.IsEnabled = !busy;
        _backButton.IsEnabled = !busy;
        _closeButton.IsEnabled = !busy;
        UpdateNavigationAvailability();
    }

    private void UpdateNavigationAvailability()
    {
        var finalStepNeedsRoot = _pageIndex == Pages.Length - 1 &&
                                 _rootSelectionRequired &&
                                 !_hasValidArchiveRoot;
        _nextButton.IsEnabled = !_rootSelectionInProgress && !finalStepNeedsRoot;
    }

    private void RenderDots()
    {
        _dots.Children.Clear();
        for (var index = 0; index < Pages.Length; index++)
        {
            _dots.Children.Add(new Border
            {
                Width = index == _pageIndex ? 20 : 7,
                Height = 7,
                Margin = new Thickness(0, 0, 6, 0),
                CornerRadius = new CornerRadius(3.5),
                Background = UiVisuals.Brush(index == _pageIndex
                    ? UiVisuals.Accent
                    : Color.FromRgb(204, 215, 227))
            });
        }
    }

    private static Border CreateBullet(string text)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(25) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var marker = new Border
        {
            Width = 18,
            Height = 18,
            CornerRadius = new CornerRadius(9),
            Background = UiVisuals.Brush(Color.FromRgb(220, 237, 255)),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock
            {
                Text = "✓",
                FontFamily = new FontFamily("Segoe UI Symbol"),
                FontSize = 10,
                Foreground = UiVisuals.Brush(UiVisuals.Accent),
                HorizontalAlignment = WpfHorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        grid.Children.Add(marker);
        var label = new TextBlock
        {
            Text = text,
            FontSize = 10.7,
            Foreground = UiVisuals.Brush(Color.FromRgb(69, 84, 101)),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            LineHeight = 17
        };
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);
        return new Border
        {
            Margin = new Thickness(0, 0, 0, 8),
            Child = grid
        };
    }

    private FrameworkElement CreateIllustration(GuideIllustration illustration)
    {
        return illustration switch
        {
            GuideIllustration.Wheel => CreateWheelIllustration(),
            GuideIllustration.Navigation => CreateNavigationIllustration(),
            GuideIllustration.Actions => CreateActionsIllustration(),
            _ => CreateTrayIllustration()
        };
    }

    private static FrameworkElement CreateWheelIllustration()
    {
        var canvas = CreateIllustrationCanvas();
        AddRing(canvas, 260, 13, Color.FromRgb(169, 212, 250));
        AddRing(canvas, 204, 12, Color.FromRgb(194, 225, 251));
        AddRing(canvas, 148, 11, Color.FromRgb(218, 237, 253));
        var center = new Border
        {
            Width = 104,
            Height = 104,
            CornerRadius = new CornerRadius(52),
            Background = UiVisuals.Brush(Color.FromArgb(245, 250, 252, 255)),
            BorderBrush = UiVisuals.Brush(Color.FromRgb(183, 207, 234)),
            BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 3, Opacity = 0.15 },
            Child = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new TextBlock
                    {
                        Text = "中键",
                        HorizontalAlignment = WpfHorizontalAlignment.Center,
                        FontSize = 16,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = UiVisuals.Brush(UiVisuals.TextPrimary)
                    },
                    new TextBlock
                    {
                        Text = "打开轮盘",
                        Margin = new Thickness(0, 2, 0, 0),
                        HorizontalAlignment = WpfHorizontalAlignment.Center,
                        FontSize = 9.5,
                        Foreground = UiVisuals.Brush(UiVisuals.TextSecondary)
                    }
                }
            }
        };
        Canvas.SetLeft(center, 96);
        Canvas.SetTop(center, 96);
        canvas.Children.Add(center);
        return canvas;
    }

    private static FrameworkElement CreateNavigationIllustration()
    {
        var canvas = CreateIllustrationCanvas();
        var labels = new[] { "公司", "项目", "资料" };
        for (var index = 0; index < labels.Length; index++)
        {
            var card = new Border
            {
                Width = 170,
                Height = 64,
                CornerRadius = new CornerRadius(14),
                Background = UiVisuals.Brush(index == 2
                    ? Color.FromRgb(220, 237, 255)
                    : Color.FromRgb(249, 251, 254)),
                BorderBrush = UiVisuals.Brush(index == 2 ? UiVisuals.Accent : UiVisuals.BorderColor),
                BorderThickness = new Thickness(index == 2 ? 1.2 : 0.8),
                Effect = new DropShadowEffect { BlurRadius = 12, ShadowDepth = 3, Opacity = 0.1 },
                Child = new Grid
                {
                    Children =
                    {
                        new TextBlock
                        {
                            Text = labels[index],
                            Margin = new Thickness(18, 0, 0, 0),
                            VerticalAlignment = VerticalAlignment.Center,
                            FontSize = 14,
                            FontWeight = FontWeights.SemiBold,
                            Foreground = UiVisuals.Brush(UiVisuals.TextPrimary)
                        },
                        new TextBlock
                        {
                            Text = "›",
                            Margin = new Thickness(0, 0, 17, 0),
                            HorizontalAlignment = WpfHorizontalAlignment.Right,
                            VerticalAlignment = VerticalAlignment.Center,
                            FontFamily = new FontFamily("Segoe UI"),
                            FontSize = 23,
                            Foreground = UiVisuals.Brush(UiVisuals.Accent)
                        }
                    }
                }
            };
            Canvas.SetLeft(card, 34 + index * 37);
            Canvas.SetTop(card, 43 + index * 78);
            canvas.Children.Add(card);
        }
        return canvas;
    }

    private static FrameworkElement CreateActionsIllustration()
    {
        var canvas = CreateIllustrationCanvas();
        var actions = new[]
        {
            ("左键", "确认移动", UiVisuals.Accent),
            ("右键", "返回上级", Color.FromRgb(100, 135, 171)),
            ("Ctrl + 左键", "新建文件夹", Color.FromRgb(242, 166, 76))
        };
        for (var index = 0; index < actions.Length; index++)
        {
            var (key, text, accent) = actions[index];
            var card = new Border
            {
                Width = 230,
                Height = 66,
                CornerRadius = new CornerRadius(15),
                Background = UiVisuals.Brush(Color.FromRgb(250, 252, 255)),
                BorderBrush = UiVisuals.Brush(Color.FromArgb(150, accent.R, accent.G, accent.B)),
                BorderThickness = new Thickness(0.9),
                Effect = new DropShadowEffect { BlurRadius = 12, ShadowDepth = 3, Opacity = 0.09 }
            };
            var grid = new Grid { Margin = new Thickness(12, 9, 15, 9) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var keyCap = new Border
            {
                CornerRadius = new CornerRadius(9),
                Background = UiVisuals.Brush(Color.FromArgb(30, accent.R, accent.G, accent.B)),
                Child = new TextBlock
                {
                    Text = key,
                    HorizontalAlignment = WpfHorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 10.5,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = UiVisuals.Brush(accent)
                }
            };
            grid.Children.Add(keyCap);
            var actionText = new TextBlock
            {
                Text = text,
                Margin = new Thickness(14, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = UiVisuals.Brush(UiVisuals.TextPrimary)
            };
            Grid.SetColumn(actionText, 1);
            grid.Children.Add(actionText);
            card.Child = grid;
            Canvas.SetLeft(card, 33);
            Canvas.SetTop(card, 35 + index * 80);
            canvas.Children.Add(card);
        }
        return canvas;
    }

    private FrameworkElement CreateTrayIllustration()
    {
        var canvas = CreateIllustrationCanvas();
        var card = new Border
        {
            Width = 250,
            Height = 265,
            CornerRadius = new CornerRadius(18),
            Background = UiVisuals.Brush(Color.FromRgb(250, 252, 255)),
            BorderBrush = UiVisuals.Brush(UiVisuals.BorderColor),
            BorderThickness = new Thickness(0.8),
            Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 5, Opacity = 0.14 }
        };
        var panel = new StackPanel { Margin = new Thickness(17, 15, 17, 14) };
        var brand = new Grid();
        brand.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(35) });
        brand.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        brand.Children.Add(new Image
        {
            Source = UiVisuals.LoadAppIcon(),
            Width = 30,
            Height = 30,
            Stretch = Stretch.Uniform
        });
        var name = new TextBlock
        {
            Text = "文件快速归档",
            Margin = new Thickness(7, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = UiVisuals.Brush(UiVisuals.TextPrimary)
        };
        Grid.SetColumn(name, 1);
        brand.Children.Add(name);
        panel.Children.Add(brand);
        panel.Children.Add(new Border
        {
            Margin = new Thickness(0, 11, 0, 8),
            Padding = new Thickness(10, 8, 10, 8),
            CornerRadius = new CornerRadius(10),
            Background = UiVisuals.Brush(UiVisuals.SurfaceMuted),
            Child = new TextBlock
            {
                Text = ArchiveScope.GetDisplayName(_rootPath),
                FontSize = 9.5,
                Foreground = UiVisuals.Brush(UiVisuals.TextPrimary),
                TextTrimming = TextTrimming.CharacterEllipsis
            }
        });
        foreach (var label in new[] { "更改轮盘默认起点", "新手引导", "暂停中键监听" })
        {
            panel.Children.Add(new Border
            {
                Height = 42,
                CornerRadius = new CornerRadius(9),
                Background = label == "新手引导"
                    ? UiVisuals.Brush(Color.FromRgb(232, 242, 253))
                    : Brushes.Transparent,
                Child = new TextBlock
                {
                    Text = label,
                    Margin = new Thickness(11, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 10.5,
                    Foreground = UiVisuals.Brush(UiVisuals.TextPrimary)
                }
            });
        }
        card.Child = panel;
        Canvas.SetLeft(card, 23);
        Canvas.SetTop(card, 16);
        canvas.Children.Add(card);
        return canvas;
    }

    private static Canvas CreateIllustrationCanvas() =>
        new()
        {
            Width = 300,
            Height = 300,
            HorizontalAlignment = WpfHorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent
        };

    private static void AddRing(
        Canvas canvas,
        double size,
        double thickness,
        Color color)
    {
        var ring = new Ellipse
        {
            Width = size,
            Height = size,
            Stroke = UiVisuals.Brush(color),
            StrokeThickness = thickness,
            StrokeDashArray = [2.8, 0.35],
            StrokeDashCap = PenLineCap.Round,
            Opacity = 0.72
        };
        Canvas.SetLeft(ring, (300 - size) / 2);
        Canvas.SetTop(ring, (300 - size) / 2);
        canvas.Children.Add(ring);
    }

    private void Complete()
    {
        if (_rootSelectionInProgress) return;
        if (_rootSelectionRequired && !_hasValidArchiveRoot)
        {
            if (_pageIndex != Pages.Length - 1)
            {
                _pageIndex = Pages.Length - 1;
                RenderPage(true, 1);
            }
            _rootSetupStatus.Text = "请先选择一个有效的轮盘起点";
            _rootSetupStatus.Foreground = UiVisuals.Brush(Color.FromRgb(190, 70, 66));
            _chooseRootButton.Focus();
            return;
        }
        Completed = true;
        DialogResult = true;
    }

    private void PlayWindowOpen()
    {
        _surface.RenderTransformOrigin = new Point(0.5, 0.5);
        var scale = new ScaleTransform(0.975, 0.975);
        _surface.RenderTransform = scale;
        Opacity = 0;
        BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(170))
            {
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut }
            });
        scale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.975, 1, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
        scale.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.975, 1, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_rootSelectionInProgress && e.Key is Key.Escape or Key.Left or Key.Right)
        {
            _rootSetupStatus.Text = "正在读取目录，请稍候";
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.Left)
        {
            ChangePage(-1);
            e.Handled = true;
        }
        else if (e.Key == Key.Right)
        {
            ChangePage(1);
            e.Handled = true;
        }
    }

    private sealed record GuidePage(
        string Title,
        string Subtitle,
        IReadOnlyList<string> Bullets,
        GuideIllustration Illustration);

    private enum GuideIllustration
    {
        Wheel,
        Navigation,
        Actions,
        Tray
    }
}
