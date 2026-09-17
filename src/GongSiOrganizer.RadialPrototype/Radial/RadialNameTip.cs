using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using GongSiOrganizer.Core;
using GongSiOrganizer.RadialPrototype.Presentation;
using Point = System.Windows.Point;
using ToolTip = System.Windows.Controls.ToolTip;

namespace GongSiOrganizer.RadialPrototype.Radial;

internal sealed class RadialNameTip : ToolTip
{
    public RadialNameTip(FrameworkElement owner)
    {
        PlacementTarget = owner;
        Placement = PlacementMode.RelativePoint;
        StaysOpen = true;
        IsHitTestVisible = false;
        Focusable = false;
        HasDropShadow = false;
        Background = System.Windows.Media.Brushes.Transparent;
        BorderThickness = new Thickness(0);
        Padding = new Thickness(0);
    }

    public void ShowName(FolderMenuNode? folder, string root, IReadOnlyList<string> sources, Point point)
    {
        var stack = new StackPanel { MaxWidth = 310 };
        void Add(string text, double size, string color, FontWeight weight)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            stack.Children.Add(new TextBlock
            {
                Text = text, FontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei UI"),
                FontSize = size, FontWeight = weight, TextWrapping = TextWrapping.Wrap,
                Foreground = (SolidColorBrush)new BrushConverter().ConvertFromString(color)!,
                Margin = new Thickness(0, stack.Children.Count == 0 ? 0 : 5, 0, 0)
            });
        }
        Add(folder?.DisplayName ?? "选择归档目录", 13, "#253C55", FontWeights.SemiBold);
        Add(DisplayPath(folder, root), 11, "#60768E", FontWeights.Normal);
        if (sources.Count > 0)
            Add("待移动：" + Path.GetFileName(sources[0]) + (sources.Count > 1 ? $" 等 {sources.Count} 个项目" : ""),
                11, "#60768E", FontWeights.Normal);
        var border = new Border
        {
            Child = stack, CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 11, 14, 11),
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(248, 251, 255)),
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(199, 217, 239)),
            BorderThickness = new Thickness(1)
        };
        Content = border;
        HorizontalOffset = point.X + 18;
        VerticalOffset = point.Y + 22;
        IsOpen = true;
        UiMotion.Reveal(border, 5, UiMotion.Hover);
    }

    internal static string DisplayPath(FolderMenuNode? node, string root)
    {
        if (node is null) return ArchiveScope.IsThisPc(root) ? "此电脑" : root;
        if (!ArchiveScope.IsThisPc(root)) return Path.Combine(root, node.RelativePath);
        var token = node.RelativePath;
        return token.Length == 0 ? "此电脑" : token[..1] + ":\\" + (token.Length > 2 ? token[2..] : "");
    }
}
