using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using GongSiOrganizer.Core;
using GongSiOrganizer.RadialPrototype.Presentation;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using Image = System.Windows.Controls.Image;
using Point = System.Windows.Point;
using WpfCursors = System.Windows.Input.Cursors;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;

namespace GongSiOrganizer.RadialPrototype.Services;

internal sealed class TrayMenuWindow : Window
{
    private const double MenuWidth = 280;
    private readonly TextBlock _rootText;
    private readonly ToggleVisual _successToggle;
    private readonly ToggleVisual _pauseToggle;
    private readonly Action<bool> _setPaused;
    private readonly Border _surface;
    private bool _successNoticesEnabled;
    private bool _paused;

    public TrayMenuWindow(
        string initialRoot,
        Action showDemo,
        Action refresh,
        Action changeRoot,
        Action resetUsage,
        Action showOnboarding,
        Action<bool> setPaused,
        Action exit)
    {
        _setPaused = setPaused;
        Width = MenuWidth;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = false;
        Background = UiVisuals.Brush(Color.FromRgb(249, 251, 254));
        ShowInTaskbar = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        FontFamily = UiVisuals.UiFont;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        Left = -10000;
        Top = -10000;

        var body = new StackPanel { Margin = new Thickness(12, 11, 12, 10) };
        body.Children.Add(CreateHeader());

        var rootCard = new Border
        {
            Margin = new Thickness(0, 9, 0, 7),
            Padding = new Thickness(10, 8, 8, 8),
            CornerRadius = new CornerRadius(10),
            Background = UiVisuals.Brush(UiVisuals.SurfaceMuted),
            BorderBrush = UiVisuals.Brush(Color.FromRgb(205, 225, 247)),
            BorderThickness = new Thickness(1)
        };
        var rootGrid = new Grid();
        rootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        rootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var rootStack = new StackPanel();
        rootStack.Children.Add(new TextBlock
        {
            Text = "轮盘默认起点",
            FontSize = 10,
            Foreground = UiVisuals.Brush(Color.FromRgb(86, 103, 122))
        });
        _rootText = new TextBlock
        {
            Margin = new Thickness(0, 3, 8, 0),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = UiVisuals.Brush(UiVisuals.TextPrimary),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 166
        };
        rootStack.Children.Add(_rootText);
        Grid.SetColumn(rootStack, 0);
        rootGrid.Children.Add(rootStack);
        var changeButton = UiVisuals.CreateButton("更改", false, 52, 28);
        changeButton.Padding = new Thickness(9, 2, 9, 2);
        changeButton.FontSize = 10;
        changeButton.Click += (_, _) => InvokeAndHide(changeRoot);
        Grid.SetColumn(changeButton, 1);
        rootGrid.Children.Add(changeButton);
        rootCard.Child = rootGrid;
        body.Children.Add(rootCard);

        body.Children.Add(CreateActionRow("?", "新手引导", "重新查看操作方法", () => InvokeAndHide(showOnboarding)));
        body.Children.Add(CreateActionRow("◎", "安全演示", "在鼠标位置预览轮盘", () => InvokeAndHide(showDemo)));
        body.Children.Add(CreateActionRow("↻", "刷新目录缓存", "重新读取磁盘和文件夹层级", () => InvokeAndHide(refresh)));
        body.Children.Add(CreateActionRow("☆", "重置常用排序", "清除所有目录的选择频次", () => InvokeAndHide(resetUsage)));
        body.Children.Add(CreateSeparator());

        ToggleVisual successToggle = null!;
        successToggle = CreateToggleRow(
            "✓",
            "移动成功提示",
            "默认静默，失败仍会提示",
            () =>
            {
                _successNoticesEnabled = !_successNoticesEnabled;
                UpdateToggle(successToggle, _successNoticesEnabled, false);
            });
        _successToggle = successToggle;
        body.Children.Add(_successToggle.Row);

        ToggleVisual pauseToggle = null!;
        pauseToggle = CreateToggleRow(
            "Ⅱ",
            "暂停中键监听",
            "暂停后不再弹出轮盘",
            () =>
            {
                _paused = !_paused;
                UpdateToggle(pauseToggle, _paused, true);
                _setPaused(_paused);
            });
        _pauseToggle = pauseToggle;
        body.Children.Add(_pauseToggle.Row);
        body.Children.Add(CreateSeparator());
        body.Children.Add(CreateActionRow("×", "退出文件快速归档", null, () => InvokeAndHide(exit), true));

        _surface = new Border
        {
            CornerRadius = new CornerRadius(12),
            Background = UiVisuals.Brush(Color.FromRgb(249, 251, 254)),
            BorderBrush = UiVisuals.Brush(Color.FromRgb(190, 206, 224)),
            BorderThickness = new Thickness(1),
            Child = body
        };
        Content = _surface;
        SetArchiveRoot(initialRoot);
        UpdateToggle(_successToggle, false, false, false);
        UpdateToggle(_pauseToggle, false, true, false);

        SourceInitialized += (_, _) => NativeDisplay.ConfigurePopupChrome(this);
        Deactivated += (_, _) => HideMenu();
        PreviewKeyDown += (_, args) =>
        {
            if (args.Key != Key.Escape) return;
            HideMenu();
            args.Handled = true;
        };
    }

    public bool SuccessNoticesEnabled => _successNoticesEnabled;

    public void SetArchiveRoot(string rootPath)
    {
        var displayName = ArchiveScope.GetDisplayName(rootPath);
        _rootText.Text = displayName;
        _rootText.ToolTip = displayName;
    }

    public void ShowAt(NativeScreenPoint anchor)
    {
        if (IsVisible)
        {
            HideMenu();
            return;
        }

        Opacity = 1;
        Show();
        UpdateLayout();
        NativeDisplay.PositionPopupWindow(this, anchor, ActualWidth, ActualHeight);
        Activate();
        Focus();
        PlayOpenAnimation();
    }

    public void HideMenu()
    {
        if (IsVisible) Hide();
    }

    public void SaveSnapshot(string path)
    {
        if (Content is FrameworkElement chrome)
            UiVisuals.SaveSnapshot(chrome, path);
        else
            UiVisuals.SaveSnapshot(_surface, path);
    }

    private static Grid CreateHeader()
    {
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var icon = new Image
        {
            Source = UiVisuals.LoadAppIcon(),
            Width = 34,
            Height = 34,
            Stretch = Stretch.Uniform
        };
        Grid.SetColumn(icon, 0);
        header.Children.Add(icon);
        var title = new StackPanel
        {
            Margin = new Thickness(5, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        title.Children.Add(new TextBlock
        {
            Text = "文件快速归档",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = UiVisuals.Brush(UiVisuals.TextPrimary)
        });
        title.Children.Add(new TextBlock
        {
            Text = "中键打开轮盘  ·  左键确认移动",
            Margin = new Thickness(0, 2, 0, 0),
            FontSize = 9,
            Foreground = UiVisuals.Brush(Color.FromRgb(86, 103, 122))
        });
        Grid.SetColumn(title, 1);
        header.Children.Add(title);
        return header;
    }

    private static Border CreateActionRow(
        string glyph,
        string title,
        string? description,
        Action action,
        bool danger = false)
    {
        var row = CreateRowShell();
        var grid = CreateRowGrid();
        grid.Children.Add(CreateGlyph(glyph, danger));
        var text = CreateRowText(title, description, danger);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        var chevron = new TextBlock
        {
            Text = "›",
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 17,
            Foreground = UiVisuals.Brush(Color.FromRgb(146, 160, 177)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 2, 0)
        };
        Grid.SetColumn(chevron, 2);
        grid.Children.Add(chevron);
        row.Child = grid;
        AttachRowBehavior(row, action);
        return row;
    }

    private static ToggleVisual CreateToggleRow(
        string glyph,
        string title,
        string description,
        Action toggle)
    {
        var row = CreateRowShell();
        var grid = CreateRowGrid();
        grid.Children.Add(CreateGlyph(glyph, false));
        var text = CreateRowText(title, description, false);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var thumb = new Ellipse
        {
            Width = 12,
            Height = 12,
            Fill = Brushes.White,
            HorizontalAlignment = WpfHorizontalAlignment.Left,
            Margin = new Thickness(3, 0, 0, 0),
            Effect = new DropShadowEffect
            {
                BlurRadius = 3,
                ShadowDepth = 1,
                Opacity = 0.25,
                Color = Colors.Black
            }
        };
        var track = new Border
        {
            Width = 32,
            Height = 18,
            CornerRadius = new CornerRadius(9),
            Background = UiVisuals.Brush(Color.FromRgb(190, 202, 216)),
            VerticalAlignment = VerticalAlignment.Center,
            Child = thumb
        };
        Grid.SetColumn(track, 2);
        grid.Children.Add(track);
        row.Child = grid;
        AttachRowBehavior(row, toggle);
        return new ToggleVisual(row, track, thumb);
    }

    private static Border CreateRowShell() =>
        new()
        {
            Height = 42,
            Margin = new Thickness(0, 1, 0, 1),
            Padding = new Thickness(7, 4, 7, 4),
            CornerRadius = new CornerRadius(9),
            Background = Brushes.Transparent,
            Cursor = WpfCursors.Hand
        };

    private static Grid CreateRowGrid()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        return grid;
    }

    private static Border CreateGlyph(string glyph, bool danger)
    {
        return new Border
        {
            Width = 24,
            Height = 24,
            CornerRadius = new CornerRadius(7),
            Background = UiVisuals.Brush(danger
                ? Color.FromRgb(253, 235, 235)
                : Color.FromRgb(229, 240, 253)),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = glyph,
                FontFamily = new FontFamily("Segoe UI Symbol"),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = UiVisuals.Brush(danger
                    ? Color.FromRgb(198, 71, 67)
                    : UiVisuals.Accent),
                HorizontalAlignment = WpfHorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            }
        };
    }

    private static StackPanel CreateRowText(string title, string? description, bool danger)
    {
        var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = UiVisuals.Brush(danger
                ? Color.FromRgb(182, 67, 63)
                : UiVisuals.TextPrimary)
        });
        if (!string.IsNullOrWhiteSpace(description))
        {
            panel.Children.Add(new TextBlock
            {
                Text = description,
                Margin = new Thickness(0, 1, 0, 0),
                FontSize = 9,
                Foreground = UiVisuals.Brush(Color.FromRgb(86, 103, 122))
            });
        }

        return panel;
    }

    private static Border CreateSeparator() =>
        new()
        {
            Height = 1,
            Margin = new Thickness(7, 4, 7, 4),
            Background = UiVisuals.Brush(Color.FromRgb(226, 233, 241))
        };

    private static void AttachRowBehavior(Border row, Action action)
    {
        row.MouseEnter += (_, _) =>
            row.Background = UiVisuals.Brush(Color.FromRgb(235, 242, 250));
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        row.MouseLeftButtonUp += (_, args) =>
        {
            action();
            args.Handled = true;
        };
    }

    private static void UpdateToggle(
        ToggleVisual visual,
        bool enabled,
        bool warning,
        bool animate = true)
    {
        visual.Track.Background = UiVisuals.Brush(enabled
            ? warning
                ? Color.FromRgb(242, 166, 76)
                : UiVisuals.Accent
            : Color.FromRgb(190, 202, 216));
        var target = enabled ? 14d : 0d;
        var transform = visual.Thumb.RenderTransform as TranslateTransform;
        if (transform is null)
        {
            transform = new TranslateTransform();
            visual.Thumb.RenderTransform = transform;
        }

        if (!animate)
        {
            transform.X = target;
            return;
        }

        transform.BeginAnimation(
            TranslateTransform.XProperty,
            new DoubleAnimation(target, TimeSpan.FromMilliseconds(145))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
    }

    private void InvokeAndHide(Action action)
    {
        HideMenu();
        _ = Dispatcher.BeginInvoke(action, DispatcherPriority.Background);
    }

    private void PlayOpenAnimation()
    {
        BeginAnimation(OpacityProperty, null);
        var translate = new TranslateTransform(0, 0);
        _surface.RenderTransform = translate;

        BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(125))
            {
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            });
        translate.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(6, 0, TimeSpan.FromMilliseconds(175))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            });
    }

    private sealed record ToggleVisual(Border Row, Border Track, Ellipse Thumb);
}
