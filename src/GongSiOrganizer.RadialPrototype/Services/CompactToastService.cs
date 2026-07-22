using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;
using FontFamily = System.Windows.Media.FontFamily;

namespace GongSiOrganizer.RadialPrototype.Services;

internal enum CompactToastKind
{
    Info,
    Success,
    Warning
}

internal sealed class CompactToastService : IDisposable
{
    private const int GwlExstyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExNoActivate = 0x08000000;

    private Window? _window;
    private DispatcherTimer? _closeTimer;
    private int _generation;

    public void Show(
        string title,
        string message,
        CompactToastKind kind,
        TimeSpan duration)
    {
        var dispatcher = System.Windows.Application.Current.Dispatcher;
        if (!dispatcher.CheckAccess())
        {
            _ = dispatcher.InvokeAsync(() => Show(title, message, kind, duration));
            return;
        }

        CloseCurrent();
        var generation = ++_generation;
        var accent = kind switch
        {
            CompactToastKind.Success => Color.FromRgb(78, 190, 132),
            CompactToastKind.Warning => Color.FromRgb(242, 166, 76),
            _ => Color.FromRgb(86, 158, 232)
        };

        var content = new Grid
        {
            Margin = new Thickness(12, 9, 13, 9)
        };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var marker = new Border
        {
            Width = 4,
            CornerRadius = new CornerRadius(2),
            Background = new SolidColorBrush(accent)
        };
        Grid.SetColumn(marker, 0);
        content.Children.Add(marker);

        var text = new StackPanel();
        text.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = Brushes.White,
            FontFamily = new FontFamily("Microsoft YaHei UI"),
            FontSize = 12.2,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        if (!string.IsNullOrWhiteSpace(message))
        {
            text.Children.Add(new TextBlock
            {
                Text = message,
                Margin = new Thickness(0, 2, 0, 0),
                Foreground = new SolidColorBrush(Color.FromRgb(205, 214, 224)),
                FontFamily = new FontFamily("Microsoft YaHei UI"),
                FontSize = 10.8,
                LineHeight = 15,
                MaxHeight = 31,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
        }

        Grid.SetColumn(text, 2);
        content.Children.Add(text);
        var border = new Border
        {
            Width = 258,
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromArgb(246, 31, 38, 48)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(100, 111, 130, 151)),
            BorderThickness = new Thickness(0.7),
            Child = content,
            Effect = new DropShadowEffect
            {
                BlurRadius = 15,
                ShadowDepth = 3,
                Opacity = 0.24,
                Color = Colors.Black
            }
        };
        var translate = new TranslateTransform(0, 7);
        border.RenderTransform = translate;

        var window = new Window
        {
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            SizeToContent = SizeToContent.WidthAndHeight,
            Content = border,
            Opacity = 0
        };
        window.SourceInitialized += (_, _) => MakeClickThrough(window);
        window.Loaded += (_, _) =>
        {
            var workArea = SystemParameters.WorkArea;
            window.Left = workArea.Right - window.ActualWidth - 14;
            window.Top = workArea.Bottom - window.ActualHeight - 14;
            window.BeginAnimation(
                UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(135))
                {
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut }
                });
            translate.BeginAnimation(
                TranslateTransform.YProperty,
                new DoubleAnimation(7, 0, TimeSpan.FromMilliseconds(180))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
        };
        _window = window;
        window.Show();

        _closeTimer = new DispatcherTimer { Interval = duration };
        _closeTimer.Tick += (_, _) => BeginClose(generation);
        _closeTimer.Start();
    }

    private void BeginClose(int generation)
    {
        if (generation != _generation || _window is null) return;
        _closeTimer?.Stop();
        var window = _window;
        var animation = new DoubleAnimation(0, TimeSpan.FromMilliseconds(130))
        {
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseIn }
        };
        animation.Completed += (_, _) =>
        {
            if (generation != _generation) return;
            window.Close();
            if (ReferenceEquals(_window, window)) _window = null;
        };
        window.BeginAnimation(UIElement.OpacityProperty, animation);
    }

    private void CloseCurrent()
    {
        _closeTimer?.Stop();
        _closeTimer = null;
        if (_window is null) return;
        _window.Close();
        _window = null;
    }

    private static void MakeClickThrough(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        var style = GetWindowLongPtr(handle, GwlExstyle).ToInt64();
        _ = SetWindowLongPtr(
            handle,
            GwlExstyle,
            new IntPtr(style | WsExTransparent | WsExNoActivate));
    }

    public void Dispose()
    {
        _generation++;
        CloseCurrent();
    }

    private static IntPtr GetWindowLongPtr(IntPtr window, int index) =>
        IntPtr.Size == 8
            ? GetWindowLongPtr64(window, index)
            : new IntPtr(GetWindowLong32(window, index));

    private static IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value) =>
        IntPtr.Size == 8
            ? SetWindowLongPtr64(window, index, value)
            : new IntPtr(SetWindowLong32(window, index, value.ToInt32()));

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong32(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern int SetWindowLong32(IntPtr window, int index, int value);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr window, int index, IntPtr value);
}
