using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;
using Cursors = System.Windows.Input.Cursors;
using FontFamily = System.Windows.Media.FontFamily;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfBorder = System.Windows.Controls.Border;
using WpfButton = System.Windows.Controls.Button;
using WpfControlTemplate = System.Windows.Controls.ControlTemplate;
using WpfGrid = System.Windows.Controls.Grid;
using WpfStackPanel = System.Windows.Controls.StackPanel;
using WpfTextBlock = System.Windows.Controls.TextBlock;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace GongSiOrganizer.RadialPrototype.Radial;

internal sealed class FolderNameDialog : Window
{
    private static readonly FontFamily UiFont = new("Microsoft YaHei UI");
    private readonly Func<string, string?> _tryCreate;
    private readonly WpfTextBox _nameBox;
    private readonly WpfTextBlock _errorText;
    private readonly WpfButton _createButton;
    private bool _submitting;

    private FolderNameDialog(
        Window owner,
        string parentRelativePath,
        Func<string, string?> tryCreate)
    {
        Owner = owner;
        _tryCreate = tryCreate;
        Title = "新建文件夹";
        Width = 360;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        FontFamily = UiFont;

        var panel = new WpfStackPanel { Margin = new Thickness(20, 17, 20, 16) };
        panel.Children.Add(new WpfTextBlock
        {
            Text = "新建文件夹",
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(34, 46, 59))
        });
        panel.Children.Add(new WpfTextBlock
        {
            Text = "位置：" + parentRelativePath.Replace("\\", " › "),
            Margin = new Thickness(0, 4, 0, 12),
            FontSize = 10.5,
            Foreground = new SolidColorBrush(Color.FromRgb(103, 117, 134)),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 318
        });

        _nameBox = new WpfTextBox
        {
            Text = "新建文件夹",
            MaxLength = GongSiOrganizer.Core.SubfolderCreationService.MaximumNameLength,
            Padding = new Thickness(10, 5, 10, 5),
            FontSize = 12.5,
            VerticalContentAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromRgb(31, 43, 57)),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CaretBrush = new SolidColorBrush(Color.FromRgb(40, 122, 214))
        };
        panel.Children.Add(new WpfBorder
        {
            Height = 36,
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromRgb(251, 253, 255)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(104, 157, 216)),
            BorderThickness = new Thickness(1.1),
            Child = _nameBox
        });

        _errorText = new WpfTextBlock
        {
            MinHeight = 28,
            Margin = new Thickness(1, 5, 1, 1),
            FontSize = 10.5,
            Foreground = new SolidColorBrush(Color.FromRgb(194, 67, 60)),
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Hidden
        };
        panel.Children.Add(_errorText);

        var actions = new WpfGrid { Margin = new Thickness(0, 2, 0, 0) };
        actions.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        actions.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition
        {
            Width = GridLength.Auto
        });
        actions.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition
        {
            Width = GridLength.Auto
        });
        var keyHint = new WpfTextBlock
        {
            Text = "Enter 创建   Esc 取消",
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 9.5,
            Foreground = new SolidColorBrush(Color.FromRgb(125, 138, 153))
        };
        System.Windows.Controls.Grid.SetColumn(keyHint, 0);
        actions.Children.Add(keyHint);

        var cancelButton = CreateButton("取消", false);
        cancelButton.IsCancel = true;
        cancelButton.Click += (_, _) => DialogResult = false;
        System.Windows.Controls.Grid.SetColumn(cancelButton, 1);
        actions.Children.Add(cancelButton);

        _createButton = CreateButton("创建", true);
        _createButton.IsDefault = true;
        _createButton.Click += (_, _) => Submit();
        System.Windows.Controls.Grid.SetColumn(_createButton, 2);
        actions.Children.Add(_createButton);
        panel.Children.Add(actions);

        Content = new WpfBorder
        {
            CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(Color.FromArgb(250, 245, 248, 252)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(170, 171, 190, 211)),
            BorderThickness = new Thickness(0.8),
            Effect = new DropShadowEffect
            {
                BlurRadius = 22,
                ShadowDepth = 5,
                Opacity = 0.25,
                Color = Colors.Black
            },
            Child = panel
        };

        Loaded += (_, _) =>
        {
            GongSiOrganizer.RadialPrototype.Presentation.UiMotion.Reveal((FrameworkElement)Content, 12, 300);
            _nameBox.Focus();
            _nameBox.SelectAll();
        };
        PreviewKeyDown += OnPreviewKeyDown;
    }

    public static bool Show(
        Window owner,
        string parentRelativePath,
        Func<string, string?> tryCreate)
    {
        var dialog = new FolderNameDialog(owner, parentRelativePath, tryCreate);
        return dialog.ShowDialog() == true;
    }

    private void Submit()
    {
        if (_submitting) return;
        _submitting = true;
        _createButton.IsEnabled = false;
        try
        {
            var error = _tryCreate(_nameBox.Text);
            if (string.IsNullOrWhiteSpace(error))
            {
                DialogResult = true;
                return;
            }

            _errorText.Text = error;
            _errorText.Visibility = Visibility.Visible;
            _nameBox.Focus();
            _nameBox.SelectAll();
        }
        finally
        {
            _submitting = false;
            _createButton.IsEnabled = true;
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            Submit();
            e.Handled = true;
        }
    }

    private static WpfButton CreateButton(string text, bool primary) =>
        new()
        {
            Content = text,
            MinWidth = 72,
            Height = 30,
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(14, 3, 14, 3),
            FontFamily = UiFont,
            FontSize = 11,
            Foreground = primary
                ? Brushes.White
                : new SolidColorBrush(Color.FromRgb(64, 78, 94)),
            Background = primary
                ? new SolidColorBrush(Color.FromRgb(53, 132, 220))
                : new SolidColorBrush(Color.FromRgb(235, 240, 246)),
            BorderBrush = primary
                ? new SolidColorBrush(Color.FromRgb(42, 112, 193))
                : new SolidColorBrush(Color.FromRgb(184, 197, 212)),
            BorderThickness = new Thickness(0.8),
            Cursor = Cursors.Hand,
            Template = CreateButtonTemplate(primary)
        };

    private static WpfControlTemplate CreateButtonTemplate(bool primary)
    {
        var template = new WpfControlTemplate(typeof(WpfButton));
        var border = new FrameworkElementFactory(typeof(WpfBorder));
        border.SetValue(WpfBorder.CornerRadiusProperty, new CornerRadius(7));
        border.SetBinding(
            WpfBorder.BackgroundProperty,
            new System.Windows.Data.Binding("Background")
            {
                RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent
            });
        border.SetBinding(
            WpfBorder.BorderBrushProperty,
            new System.Windows.Data.Binding("BorderBrush")
            {
                RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent
            });
        border.SetBinding(
            WpfBorder.BorderThicknessProperty,
            new System.Windows.Data.Binding("BorderThickness")
            {
                RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent
            });

        var presenter = new FrameworkElementFactory(typeof(System.Windows.Controls.ContentPresenter));
        presenter.SetValue(
            FrameworkElement.HorizontalAlignmentProperty,
            System.Windows.HorizontalAlignment.Center);
        presenter.SetValue(
            FrameworkElement.VerticalAlignmentProperty,
            System.Windows.VerticalAlignment.Center);
        border.AppendChild(presenter);
        template.VisualTree = border;

        var hover = new Trigger
        {
            Property = IsMouseOverProperty,
            Value = true
        };
        hover.Setters.Add(new Setter(
            System.Windows.Controls.Control.BackgroundProperty,
            new SolidColorBrush(primary
                ? Color.FromRgb(65, 145, 231)
                : Color.FromRgb(226, 234, 243))));
        template.Triggers.Add(hover);

        var pressed = new Trigger
        {
            Property = System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty,
            Value = true
        };
        pressed.Setters.Add(new Setter(
            System.Windows.Controls.Control.BackgroundProperty,
            new SolidColorBrush(primary
                ? Color.FromRgb(38, 113, 198)
                : Color.FromRgb(211, 222, 235))));
        template.Triggers.Add(pressed);
        return template;
    }
}
