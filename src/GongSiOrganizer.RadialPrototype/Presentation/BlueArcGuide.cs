using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using GongSiOrganizer.RadialPrototype.Radial;
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;

namespace GongSiOrganizer.RadialPrototype.Presentation;

// The guide owns its clock and illustration only; it never sends input to the menu.
internal sealed class BlueArcGuide : FrameworkElement
{
    private static readonly DependencyProperty PhaseProperty = DependencyProperty.Register(
        nameof(Phase), typeof(double), typeof(BlueArcGuide),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    private double Phase => (double)GetValue(PhaseProperty);
    private readonly Typeface _typeface = new(UiVisuals.UiFont, FontStyles.Normal,
        FontWeights.Medium, FontStretches.Normal);

    public BlueArcGuide()
    {
        IsHitTestVisible = false;
        Loaded += (_, _) =>
        {
            if (UiMotion.Enabled)
                BeginAnimation(PhaseProperty, new DoubleAnimation(0, 6, TimeSpan.FromSeconds(6))
                { RepeatBehavior = RepeatBehavior.Forever });
        };
        Unloaded += (_, _) => BeginAnimation(PhaseProperty, null);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var scale = Math.Min(ActualWidth / 300, ActualHeight / 320);
        if (scale <= 0) return;
        dc.PushTransform(new TranslateTransform((ActualWidth - 300 * scale) / 2,
            (ActualHeight - 320 * scale) / 2));
        dc.PushTransform(new ScaleTransform(scale, scale));
        dc.DrawRoundedRectangle(UiVisuals.Brush(UiVisuals.SurfaceMuted), null,
            new Rect(0, 0, 300, 320), 22, 22);
        var center = new RadialPoint(150, 219);
        dc.DrawGeometry(UiVisuals.Brush(Color.FromRgb(249, 252, 255)),
            new Pen(UiVisuals.Brush(UiVisuals.BorderColor), .8),
            RadialGeometry.Wedge(center, 34, 64, -134, -46, 9));
        var selected = RadialGeometry.Wedge(center, 77, 161, -134, -46, 12);
        dc.DrawGeometry(UiVisuals.Brush(Color.FromRgb(218, 234, 255)),
            new Pen(UiVisuals.Brush(Color.FromRgb(147, 184, 232)), 1),
            selected);
        var breathing = .65 + .35 * (.5 - .5 * Math.Cos(Phase * Math.PI / 1.1));
        var arc = RadialGeometry.Arc(center, 159, -130, -102);
        dc.PushOpacity(breathing);
        dc.DrawGeometry(null, new Pen(UiVisuals.Brush(Color.FromArgb(24, 31, 78, 145)), 12), arc);
        dc.DrawGeometry(null, new Pen(UiVisuals.Brush(Color.FromRgb(31, 78, 145)), 4)
        { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, arc);
        dc.Pop();

        var change = Ease((Phase - 2.85) / .55);
        if (!UiMotion.Enabled) change = 0;
        dc.PushClip(selected);
        DrawDirectoryName(dc, "推广方案\n终审修订", 150, 82 - change * 10, 1 - change);
        DrawDirectoryName(dc, "客户提案\n最终交付", 150, 82 + (1 - change) * 16, change);
        dc.Pop();
        DrawDirectoryName(dc, "年度项目", 150, 161);
        Text(dc, "第三圈及更深目录", 150, 22, 11);

        var travel = Ease((Phase - 1.75) / .8);
        var body = new Point(190, 111);
        var target = RadialGeometry.Polar(center, 159, -116);
        var pointer = new Point(body.X + (target.X - body.X) * travel,
            body.Y + (target.Y - body.Y) * travel);
        var pointerAlpha = Phase > 3.6 ? 1 - Ease((Phase - 3.6) / .45) : 1;
        dc.PushOpacity(pointerAlpha);
        dc.PushTransform(new TranslateTransform(pointer.X, pointer.Y));
        var cursor = System.Windows.Media.Geometry.Parse("M 0,0 L 0,20 L 5,15 L 9,24 L 13,22 L 9,13 L 17,13 Z");
        dc.DrawGeometry(UiVisuals.Brush(UiVisuals.TextPrimary),
            new Pen(System.Windows.Media.Brushes.White, 1.5) { LineJoin = PenLineJoin.Round }, cursor);
        dc.Pop();
        dc.Pop();
        var caption = !UiMotion.Enabled ? "停在名称上辨认\n移向蓝弧进入下一层"
            : Phase < 1.75 ? "停在名称上，安心辨认"
            : Phase < 2.85 ? "移向深蓝弧线，准备进入"
            : "已进入下一层\n稍移出蓝弧，再移回进入";
        Text(dc, caption, 150, 247, 12);
        dc.Pop();
        dc.Pop();
    }

    private static double Ease(double value) => 1 - Math.Pow(1 - Math.Clamp(value, 0, 1), 3);

    private void DrawDirectoryName(DrawingContext dc, string value, double x, double y, double opacity = 1)
    {
        var text = new FormattedText(value, CultureInfo.CurrentUICulture,
            System.Windows.FlowDirection.LeftToRight, RadialTypography.Typeface, RadialTypography.FontSize,
            UiVisuals.Brush(UiVisuals.TextPrimary), VisualTreeHelper.GetDpi(this).PixelsPerDip)
        { TextAlignment = TextAlignment.Center, MaxTextWidth = 100, LineHeight = RadialTypography.LineHeight };
        dc.PushOpacity(Math.Clamp(opacity, 0, 1));
        dc.DrawText(text, new Point(x - 50, y));
        dc.Pop();
    }

    private void Text(DrawingContext dc, string value, double x, double y, double size, double opacity = 1)
    {
        var text = new FormattedText(value, CultureInfo.CurrentUICulture,
            System.Windows.FlowDirection.LeftToRight, _typeface, size,
            UiVisuals.Brush(UiVisuals.TextPrimary), VisualTreeHelper.GetDpi(this).PixelsPerDip)
        { TextAlignment = TextAlignment.Center, MaxTextWidth = 268, LineHeight = size * 1.35 };
        dc.PushOpacity(Math.Clamp(opacity, 0, 1));
        dc.DrawText(text, new Point(x - 134, y));
        dc.Pop();
    }
}
