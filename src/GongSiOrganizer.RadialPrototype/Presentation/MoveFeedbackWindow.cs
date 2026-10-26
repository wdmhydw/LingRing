using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GongSiOrganizer.RadialPrototype.Services;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace GongSiOrganizer.RadialPrototype.Presentation;

// A separate, non-activating surface lets Explorer receive input as soon as the move starts.
// Each operation owns its window; a late completion never updates a newer menu or feedback.
internal sealed class MoveFeedbackWindow : Window
{
    private readonly MoveFeedbackControl _visual;
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = new();
    private bool _closed;
    public bool IsDismissed => _closed;

    public MoveFeedbackWindow(OverlayPlacement placement, Drawing? snapshot, bool demo = false)
    {
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        IsHitTestVisible = false;
        Topmost = true;
        Width = placement.Layout.WindowBounds.Width;
        Height = placement.Layout.WindowBounds.Height;
        _visual = new MoveFeedbackControl(snapshot,
            new Point(placement.Layout.LocalCenter.X, placement.Layout.LocalCenter.Y), demo)
        { Width = Width, Height = Height };
        _visual.Advance(0, UiMotion.Enabled);
        Content = _visual;
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += OnTick;
        SourceInitialized += (_, _) => MakeClickThrough();
        Closed += (_, _) =>
        {
            _closed = true;
            _timer.Stop();
            _timer.Tick -= OnTick;
            _clock.Stop();
            _visual.ReleaseSnapshot();
        };
        Show();
        NativeDisplay.PositionWindow(this, placement);
        _clock.Start();
        _timer.Start();
    }

    public void Complete(MoveFeedbackOutcome outcome, int moved, int total)
    {
        if (_closed) return;
        _visual.Timeline.Complete(outcome, moved, total, _clock.Elapsed.TotalMilliseconds);
        OnTick(this, EventArgs.Empty);
    }

    public void Dismiss()
    {
        if (!_closed) Close();
    }

    private void OnTick(object? sender, EventArgs args)
    {
        if (_closed) return;
        var animate = UiMotion.Enabled;
        _timer.Interval = TimeSpan.FromMilliseconds(animate ? 16 : 80);
        var frame = _visual.Advance(_clock.Elapsed.TotalMilliseconds, animate);
        if (frame.Finished) Dismiss();
    }

    public void SaveSnapshot(string path)
    {
        _visual.UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(_visual);
        var bitmap = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(Width * dpi.DpiScaleX)),
            Math.Max(1, (int)Math.Ceiling(Height * dpi.DpiScaleY)), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(_visual);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private void MakeClickThrough()
    {
        var handle = new WindowInteropHelper(this).Handle;
        const int exStyle = -20;
        const long transparentNoActivateToolWindow = 0x20 | 0x08000000 | 0x80;
        var style = IntPtr.Size == 8 ? GetWindowLongPtr(handle, exStyle).ToInt64() : GetWindowLong(handle, exStyle);
        if (IntPtr.Size == 8) SetWindowLongPtr(handle, exStyle, new IntPtr(style | transparentNoActivateToolWindow));
        else SetWindowLong(handle, exStyle, (int)(style | transparentNoActivateToolWindow));
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr window, int index, int value);
}

internal sealed class MoveFeedbackControl(Drawing? snapshot, Point origin, bool demo = false) : FrameworkElement
{
    private static readonly Brush PlateShadow = CreatePlateShadow();
    private Drawing? _snapshot = snapshot;
    private MoveFeedbackFrame _frame;
    private bool _animate = true;
    public MoveFeedbackTimeline Timeline { get; } = new();

    public MoveFeedbackFrame Advance(double elapsed, bool animate)
    {
        _animate = animate;
        var next = Timeline.At(elapsed, animate);
        if (next.Collapse >= 1) ReleaseSnapshot();
        if (next != _frame) { _frame = next; InvalidateVisual(); }
        return next;
    }

    public void ReleaseSnapshot() => _snapshot = null;

    internal static Point FitCenter(Point anchor, Size available)
    {
        // Keep the white rim and its soft shadow inside the clipped monitor work area.
        return new Point(Math.Clamp(anchor.X, Math.Min(80, available.Width / 2), Math.Max(available.Width / 2, available.Width - 80)),
            Math.Clamp(anchor.Y, Math.Min(58, available.Height / 2), Math.Max(available.Height / 2, available.Height - 84)));
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var center = FitCenter(origin, RenderSize);
        var amount = 1 - Math.Pow(1 - _frame.Collapse, 3);
        if (_snapshot is not null && _frame.Collapse < 1)
        {
            var scale = 1 - .82 * amount;
            dc.PushOpacity(1 - amount);
            dc.PushTransform(new TranslateTransform((center.X - origin.X) * amount, (center.Y - origin.Y) * amount));
            dc.PushTransform(new ScaleTransform(scale, scale, origin.X, origin.Y));
            dc.DrawDrawing(_snapshot);
            dc.Pop(); dc.Pop(); dc.Pop();
        }
        var color = Timeline.Outcome switch
        {
            MoveFeedbackOutcome.Success => Color.FromRgb(19, 133, 106),
            MoveFeedbackOutcome.Partial => UiVisuals.Gold,
            MoveFeedbackOutcome.Failure => Color.FromRgb(198, 75, 68),
            _ => UiVisuals.Accent
        };
        var accent = new SolidColorBrush(color);
        // The central disc survives the hand-off, even when the waiting arc is deliberately delayed.
        dc.PushOpacity(Math.Clamp((amount - .5) * 2, 0, 1) * _frame.Opacity);
        dc.DrawEllipse(PlateShadow, null, new Point(center.X, center.Y + 4), 55, 55);
        dc.DrawEllipse(System.Windows.Media.Brushes.White, null, center, 46, 46);
        if (Timeline.Outcome is null)
            dc.DrawEllipse(null, new Pen(UiVisuals.Brush(UiVisuals.SurfaceMuted), 7.5), center, 34, 34);
        dc.Pop();
        if (_frame.Badge <= 0 || (Timeline.Outcome is null && !_frame.Pending)) return;
        dc.PushOpacity(Math.Clamp(_frame.Badge, 0, 1) * _frame.Opacity);
        if (_frame.Pending)
        {
            DrawArc(dc, center, 34, _frame.SpinnerAngle, 210, new Pen(accent, 7.5));
            Text(dc, demo ? "演示中" : "移动中", center.X, center.Y - 10,
                UiVisuals.Brush(UiVisuals.TextPrimary), 14);
        }
        else
        {
            var scale = _animate ? .9 + .1 * UiMotion.Settle(_frame.Badge) : 1;
            dc.PushTransform(new ScaleTransform(scale, scale, center.X, center.Y));
            dc.DrawEllipse(accent, null, center, 37, 37);
            // A single, solid badge carries the result. Batch counts sit inside it, not on a second plate.
            var detail = demo ? "演示" : Timeline.Total > 1 || Timeline.Outcome != MoveFeedbackOutcome.Success
                ? $"{Timeline.Moved}/{Timeline.Total}" : null;
            var symbolCenter = new Point(center.X, center.Y - (detail is null ? 0 : 7));
            var foreground = System.Windows.Media.Brushes.White;
            var symbolPen = new Pen(foreground, 6)
            { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            if (Timeline.Outcome == MoveFeedbackOutcome.Success)
            {
                var tick = new StreamGeometry();
                using (var g = tick.Open())
                {
                    g.BeginFigure(new Point(symbolCenter.X - 14, symbolCenter.Y), false, false);
                    g.LineTo(new Point(symbolCenter.X - 4, symbolCenter.Y + 10), true, false);
                    g.LineTo(new Point(symbolCenter.X + 15, symbolCenter.Y - 11), true, false);
                }
                dc.DrawGeometry(null, symbolPen, tick);
            }
            else if (Timeline.Outcome == MoveFeedbackOutcome.Failure)
            {
                dc.DrawLine(symbolPen, new Point(symbolCenter.X - 10, symbolCenter.Y - 10),
                    new Point(symbolCenter.X + 10, symbolCenter.Y + 10));
                dc.DrawLine(symbolPen, new Point(symbolCenter.X + 10, symbolCenter.Y - 10),
                    new Point(symbolCenter.X - 10, symbolCenter.Y + 10));
            }
            else
            {
                dc.DrawLine(symbolPen, new Point(symbolCenter.X, symbolCenter.Y - 12), new Point(symbolCenter.X, symbolCenter.Y + 2));
                dc.DrawEllipse(foreground, null, new Point(symbolCenter.X, symbolCenter.Y + 12), 3, 3);
            }
            if (detail is not null) Text(dc, detail, center.X, center.Y + 14, foreground, 11);
            dc.Pop();
        }
        dc.Pop();
    }

    private static Brush CreatePlateShadow()
    {
        var brush = new RadialGradientBrush
        {
            GradientStops =
            [
                new GradientStop(Color.FromArgb(28, 20, 28, 40), 0),
                new GradientStop(Color.FromArgb(24, 20, 28, 40), .72),
                new GradientStop(Color.FromArgb(12, 20, 28, 40), .84),
                new GradientStop(Color.FromArgb(3, 20, 28, 40), .94),
                new GradientStop(Color.FromArgb(0, 20, 28, 40), 1)
            ]
        };
        brush.Freeze();
        return brush;
    }

    private void Text(DrawingContext dc, string value, double x, double y, Brush brush, double size)
    {
        var text = new FormattedText(value, CultureInfo.CurrentUICulture, System.Windows.FlowDirection.LeftToRight,
            new Typeface(new System.Windows.Media.FontFamily("Microsoft YaHei UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
            size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip)
        { TextAlignment = TextAlignment.Center, MaxTextWidth = 62, MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
        dc.DrawText(text, new Point(x - 31, y));
    }

    private static void DrawArc(DrawingContext dc, Point center, double radius, double start, double sweep, Pen pen)
    {
        Point At(double angle) => new(center.X + radius * Math.Cos(angle * Math.PI / 180), center.Y + radius * Math.Sin(angle * Math.PI / 180));
        var arc = new StreamGeometry();
        using (var g = arc.Open())
        {
            g.BeginFigure(At(start), false, false);
            g.ArcTo(At(start + sweep), new Size(radius, radius), 0, Math.Abs(sweep) > 180, SweepDirection.Clockwise, true, false);
        }
        pen.StartLineCap = pen.EndLineCap = PenLineCap.Round;
        dc.DrawGeometry(null, pen, arc);
    }
}
