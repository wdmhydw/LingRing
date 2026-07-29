using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using GongSiOrganizer.Core;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using Image = System.Windows.Controls.Image;

namespace GongSiOrganizer.RadialPrototype.Presentation;

internal enum ArchiveRootChoice
{
    None,
    ThisPc,
    Folder
}

internal sealed class ArchiveRootChoiceWindow : Window
{
    private ArchiveRootChoiceWindow(string? currentRoot)
    {
        Title = "设置轮盘起点";
        Width = 430;
        Height = 300;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = UiVisuals.UiFont;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;

        var activeOwner = System.Windows.Application.Current.Windows
            .OfType<Window>()
            .FirstOrDefault(window => window.IsActive && window != this);
        if (activeOwner is not null)
        {
            Owner = activeOwner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        var panel = new StackPanel { Margin = new Thickness(22, 19, 22, 18) };
        panel.Children.Add(CreateHeader());
        panel.Children.Add(CreateHierarchy(currentRoot));

        var cancel = UiVisuals.CreateButton("取消", false, 76, 30);
        cancel.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
        cancel.Margin = new Thickness(0, 9, 0, 0);
        cancel.IsCancel = true;
        cancel.FocusVisualStyle = null;
        cancel.Click += (_, _) =>
        {
            Choice = ArchiveRootChoice.None;
            DialogResult = false;
        };
        panel.Children.Add(cancel);

        Content = new Border
        {
            CornerRadius = new CornerRadius(18),
            Background = UiVisuals.Brush(Color.FromArgb(252, 248, 251, 255)),
            BorderBrush = UiVisuals.Brush(Color.FromRgb(196, 211, 228)),
            BorderThickness = new Thickness(0.9),
            Effect = new DropShadowEffect
            {
                BlurRadius = 26,
                ShadowDepth = 6,
                Opacity = 0.23,
                Color = Colors.Black
            },
            Child = panel
        };

        PreviewKeyDown += (_, args) =>
        {
            if (args.Key != Key.Escape) return;
            Choice = ArchiveRootChoice.None;
            DialogResult = false;
            args.Handled = true;
        };
    }

    public ArchiveRootChoice Choice { get; private set; }

    public static ArchiveRootChoice Show(string? currentRoot)
    {
        var window = new ArchiveRootChoiceWindow(currentRoot);
        return window.ShowDialog() == true
            ? window.Choice
            : ArchiveRootChoice.None;
    }

    public static ArchiveRootChoiceWindow CreateForPreview(string? currentRoot) =>
        new(currentRoot);

    public void SaveSnapshot(string path) =>
        UiVisuals.SaveSnapshot((FrameworkElement)Content, path);

    private static FrameworkElement CreateHeader()
    {
        var grid = new Grid { Margin = new Thickness(1, 0, 1, 13) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star)
        });

        grid.Children.Add(new Image
        {
            Source = UiVisuals.LoadAppIcon(),
            Width = 38,
            Height = 38,
            Margin = new Thickness(0, 0, 12, 0),
            SnapsToDevicePixels = true
        });

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock
        {
            Text = "设置轮盘起点",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = UiVisuals.Brush(UiVisuals.TextPrimary)
        });
        text.Children.Add(new TextBlock
        {
            Text = "从“此电脑”向下选择，选中的层级就是轮盘起点",
            Margin = new Thickness(0, 2, 0, 0),
            FontSize = 10.5,
            Foreground = UiVisuals.Brush(UiVisuals.TextSecondary)
        });
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        return grid;
    }

    private FrameworkElement CreateHierarchy(string? currentRoot)
    {
        var isWholeComputer = ArchiveScope.IsThisPc(currentRoot);
        var hasFolderStart = !string.IsNullOrWhiteSpace(currentRoot) &&
                             !isWholeComputer;
        var stack = new StackPanel();

        var computer = CreateChoiceButton(
            "此电脑（最高层）",
            "显示所有已就绪的本地磁盘，可在磁盘之间归档",
            "◎",
            isWholeComputer,
            () => Complete(ArchiveRootChoice.ThisPc));
        computer.Margin = new Thickness(0);
        stack.Children.Add(computer);

        var childHost = new Grid { Margin = new Thickness(28, 7, 0, 0) };
        childHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        childHost.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        childHost.Children.Add(new Border
        {
            Width = 15,
            Height = 39,
            Margin = new Thickness(0, -11, 7, 0),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            CornerRadius = new CornerRadius(0, 0, 0, 8),
            BorderBrush = UiVisuals.Brush(Color.FromRgb(184, 203, 224)),
            BorderThickness = new Thickness(1, 0, 0, 1)
        });

        var folder = CreateChoiceButton(
            "磁盘或文件夹（下级）",
            hasFolderStart
                ? currentRoot!
                : "向下选择某个磁盘、文件夹或更深一级目录",
            "▣",
            hasFolderStart,
            () => Complete(ArchiveRootChoice.Folder));
        folder.Margin = new Thickness(0);
        Grid.SetColumn(folder, 1);
        childHost.Children.Add(folder);
        stack.Children.Add(childHost);

        stack.Children.Add(new Border
        {
            Margin = new Thickness(50, 7, 0, 0),
            Padding = new Thickness(9, 4, 9, 4),
            CornerRadius = new CornerRadius(8),
            Background = UiVisuals.Brush(Color.FromRgb(240, 246, 253)),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            Child = new TextBlock
            {
                Text = "此电脑  ›  本地磁盘  ›  文件夹",
                FontSize = 9.2,
                Foreground = UiVisuals.Brush(Color.FromRgb(89, 117, 148))
            }
        });
        return stack;
    }

    private static Button CreateChoiceButton(
        string title,
        string subtitle,
        string glyph,
        bool selected,
        Action onClick)
    {
        var content = new Grid { Margin = new Thickness(13, 9, 12, 9) };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
        content.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        content.Children.Add(new Border
        {
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(10),
            Background = UiVisuals.Brush(selected
                ? Color.FromRgb(214, 233, 255)
                : Color.FromRgb(234, 241, 249)),
            Child = new TextBlock
            {
                Text = glyph,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                Foreground = UiVisuals.Brush(UiVisuals.Accent)
            }
        });

        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        labels.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = UiVisuals.Brush(UiVisuals.TextPrimary)
        });
        labels.Children.Add(new TextBlock
        {
            Text = subtitle,
            Margin = new Thickness(0, 2, 0, 0),
            FontSize = 9.5,
            Foreground = UiVisuals.Brush(UiVisuals.TextSecondary),
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = subtitle
        });
        Grid.SetColumn(labels, 1);
        content.Children.Add(labels);

        var state = new TextBlock
        {
            Text = selected ? "当前" : "›",
            Margin = new Thickness(9, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = selected ? 9.5 : 20,
            FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = UiVisuals.Brush(selected
                ? UiVisuals.Accent
                : Color.FromRgb(138, 156, 176))
        };
        Grid.SetColumn(state, 2);
        content.Children.Add(state);

        var button = new Button
        {
            Content = content,
            Height = 68,
            Margin = new Thickness(0, 0, 0, 8),
            Padding = new Thickness(0),
            HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            Background = UiVisuals.Brush(selected
                ? Color.FromRgb(239, 247, 255)
                : Color.FromRgb(249, 251, 254)),
            BorderBrush = UiVisuals.Brush(selected
                ? Color.FromRgb(116, 171, 230)
                : UiVisuals.BorderColor),
            BorderThickness = new Thickness(selected ? 1.2 : 0.8),
            Cursor = System.Windows.Input.Cursors.Hand,
            FocusVisualStyle = null,
            Template = CreateChoiceTemplate()
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    private static ControlTemplate CreateChoiceTemplate()
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(12));
        border.SetBinding(
            Border.BackgroundProperty,
            new System.Windows.Data.Binding("Background")
            {
                RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent
            });
        border.SetBinding(
            Border.BorderBrushProperty,
            new System.Windows.Data.Binding("BorderBrush")
            {
                RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent
            });
        border.SetBinding(
            Border.BorderThicknessProperty,
            new System.Windows.Data.Binding("BorderThickness")
            {
                RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent
            });
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(
            FrameworkElement.HorizontalAlignmentProperty,
            System.Windows.HorizontalAlignment.Stretch);
        presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Stretch);
        border.AppendChild(presenter);
        template.VisualTree = border;

        var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(
            System.Windows.Controls.Control.BackgroundProperty,
            UiVisuals.Brush(Color.FromRgb(242, 248, 255))));
        hover.Setters.Add(new Setter(
            System.Windows.Controls.Control.BorderBrushProperty,
            UiVisuals.Brush(Color.FromRgb(126, 177, 232))));
        template.Triggers.Add(hover);
        return template;
    }

    private void Complete(ArchiveRootChoice choice)
    {
        Choice = choice;
        DialogResult = true;
    }
}
