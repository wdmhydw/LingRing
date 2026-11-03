using System.Windows;
using System.Windows.Controls;
using GongSiOrganizer.RadialPrototype.Services;
using ComboBox = System.Windows.Controls.ComboBox;
using Orientation = System.Windows.Controls.Orientation;

namespace GongSiOrganizer.RadialPrototype.Presentation;

internal sealed class RadialSettingsWindow : Window
{
    private readonly ComboBox _direction;
    private readonly ComboBox _quantity;

    public RadialSettingsWindow(RadialPreferences preferences, Func<RadialPreferences, bool> save)
    {
        Title = "轮盘显示 · 灵环";
        Width = 440;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = UiVisuals.Brush(UiVisuals.Surface);
        FontFamily = UiVisuals.UiFont;
        ShowInTaskbar = true;
        Topmost = true;
        var body = new StackPanel();
        Content = new Border { Padding = new Thickness(26, 23, 26, 22),
            Background = UiVisuals.Brush(UiVisuals.Surface), Child = body };
        body.Children.Add(Text("按你的习惯阅读目录", 20, 0));
        body.Children.Add(Text("保存后，下次打开轮盘即生效。", 12, 8, muted: true));
        body.Children.Add(Text("文字排布", 13, 24));
        _direction = new ComboBox
        {
            ItemsSource = new[] { "沿弧排字（默认）", "原版排字" },
            SelectedIndex = preferences.CurvedLabels ? 0 : 1,
            Margin = new Thickness(0, 8, 0, 0), Padding = new Thickness(10, 7, 10, 7), FontSize = 13
        };
        body.Children.Add(_direction);
        body.Children.Add(Text("沿弧排字保留圆环弧度，长名称按扇区空间换行。", 11.5, 8, muted: true));
        body.Children.Add(Text("每圈最多显示", 13, 20));
        _quantity = new ComboBox
        {
            ItemsSource = Enumerable.Range(2, 7).Select(count => count == 4 ? "4 项（默认）" : $"{count} 项").ToArray(),
            SelectedIndex = preferences.Normalize().MaximumItems - 2,
            Margin = new Thickness(0, 8, 0, 0), Padding = new Thickness(10, 7, 10, 7), FontSize = 13
        };
        body.Children.Add(_quantity);
        body.Children.Add(Text("数量越多，单项空间越小；超出部分仍可滚轮切换。", 11.5, 8, muted: true));
        var status = Text("", 11.5, 10);
        status.Foreground = UiVisuals.Brush(System.Windows.Media.Color.FromRgb(176, 52, 48));
        body.Children.Add(status);
        var actions = new StackPanel { Orientation = Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var reset = UiVisuals.CreateButton("恢复默认", false, 90, 34);
        reset.Click += (_, _) => { _direction.SelectedIndex = 0; _quantity.SelectedIndex = 2; };
        var cancel = UiVisuals.CreateButton("取消", false, 64, 34);
        cancel.Margin = new Thickness(8, 0, 0, 0);
        cancel.IsCancel = true;
        cancel.Click += (_, _) => Close();
        var apply = UiVisuals.CreateButton("保存", true, 70, 34);
        apply.Margin = new Thickness(8, 0, 0, 0);
        apply.IsDefault = true;
        apply.Click += (_, _) =>
        {
            if (save(new RadialPreferences(_quantity.SelectedIndex + 2, _direction.SelectedIndex == 0))) Close();
            else status.Text = "保存失败，请检查设置目录的写入权限后重试。";
        };
        actions.Children.Add(reset); actions.Children.Add(cancel); actions.Children.Add(apply);
        body.Children.Add(actions);
    }

    public void SaveSnapshot(string path) => UiVisuals.SaveSnapshot((FrameworkElement)Content, path);

    private static TextBlock Text(string text, double size, double top, bool muted = false) => new()
    {
        Text = text, FontSize = size, Margin = new Thickness(0, top, 0, 0), TextWrapping = TextWrapping.Wrap,
        Foreground = UiVisuals.Brush(muted ? UiVisuals.TextSecondary : UiVisuals.TextPrimary)
    };
}
