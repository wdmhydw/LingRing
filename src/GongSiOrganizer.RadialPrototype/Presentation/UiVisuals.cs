using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
using Button = System.Windows.Controls.Button;
using Binding = System.Windows.Data.Binding;
using Border = System.Windows.Controls.Border;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Control = System.Windows.Controls.Control;
using FontFamily = System.Windows.Media.FontFamily;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;

namespace GongSiOrganizer.RadialPrototype.Presentation;

internal static class UiVisuals
{
    public static readonly FontFamily UiFont = new("Microsoft YaHei UI");
    public static readonly Color Canvas = Color.FromRgb(244, 247, 251);
    public static readonly Color Surface = Color.FromRgb(252, 253, 255);
    public static readonly Color SurfaceMuted = Color.FromRgb(237, 244, 252);
    public static readonly Color TextPrimary = Color.FromRgb(31, 44, 59);
    public static readonly Color TextSecondary = Color.FromRgb(101, 117, 136);
    public static readonly Color BorderColor = Color.FromRgb(205, 218, 233);
    public static readonly Color Accent = Color.FromRgb(49, 132, 226);
    public static readonly Color AccentHover = Color.FromRgb(64, 145, 232);
    public static readonly Color AccentPressed = Color.FromRgb(38, 112, 198);
    public static readonly Color Gold = Color.FromRgb(255, 184, 24);

    public static SolidColorBrush Brush(Color color) => new(color);

    public static BitmapImage LoadAppIcon()
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri(
            "pack://application:,,,/Assets/GongSiOrganizer.png",
            UriKind.Absolute);
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        image.Freeze();
        return image;
    }

    public static Button CreateButton(
        string text,
        bool primary,
        double minWidth = 92,
        double height = 38)
    {
        var button = new Button
        {
            Content = text,
            MinWidth = minWidth,
            Height = height,
            Padding = new Thickness(17, 4, 17, 4),
            FontFamily = UiFont,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = primary ? Brushes.White : Brush(TextPrimary),
            Background = primary ? Brush(Accent) : Brush(Color.FromRgb(234, 240, 247)),
            BorderBrush = primary ? Brush(Color.FromRgb(42, 112, 194)) : Brush(BorderColor),
            BorderThickness = new Thickness(0.8),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        button.Template = CreateButtonTemplate(primary);
        var scale = new ScaleTransform(1, 1);
        button.RenderTransformOrigin = new System.Windows.Point(.5, .5);
        button.RenderTransform = scale;
        void Animate(double target)
        {
            if (!UiMotion.Enabled) return;
            var animation = new DoubleAnimation(target, TimeSpan.FromMilliseconds(180))
            { EasingFunction = UiMotion.EaseOut() };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
        }
        button.MouseEnter += (_, _) => Animate(1.025);
        button.MouseLeave += (_, _) => Animate(1);
        button.PreviewMouseLeftButtonDown += (_, _) => Animate(.965);
        button.PreviewMouseLeftButtonUp += (_, _) => Animate(button.IsMouseOver ? 1.025 : 1);
        button.LostMouseCapture += (_, _) => Animate(button.IsMouseOver ? 1.025 : 1);
        return button;
    }

    public static void SaveSnapshot(FrameworkElement element, string path)
    {
        element.UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(element);
        var width = Math.Max(1, (int)Math.Ceiling(element.ActualWidth * dpi.DpiScaleX));
        var height = Math.Max(1, (int)Math.Ceiling(element.ActualHeight * dpi.DpiScaleY));
        var bitmap = new RenderTargetBitmap(
            width,
            height,
            dpi.PixelsPerInchX,
            dpi.PixelsPerInchY,
            PixelFormats.Pbgra32);
        bitmap.Render(element);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static ControlTemplate CreateButtonTemplate(bool primary)
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
        border.SetBinding(
            Border.BackgroundProperty,
            new Binding("Background") { RelativeSource = RelativeSource.TemplatedParent });
        border.SetBinding(
            Border.BorderBrushProperty,
            new Binding("BorderBrush") { RelativeSource = RelativeSource.TemplatedParent });
        border.SetBinding(
            Border.BorderThicknessProperty,
            new Binding("BorderThickness") { RelativeSource = RelativeSource.TemplatedParent });

        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, WpfHorizontalAlignment.Center);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        template.VisualTree = border;

        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(
            Control.BackgroundProperty,
            Brush(primary ? AccentHover : Color.FromRgb(224, 234, 244))));
        template.Triggers.Add(hover);

        var pressed = new Trigger
        {
            Property = System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty,
            Value = true
        };
        pressed.Setters.Add(new Setter(
            Control.BackgroundProperty,
            Brush(primary ? AccentPressed : Color.FromRgb(211, 224, 238))));
        template.Triggers.Add(pressed);

        return template;
    }
}
