using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using GongSiOrganizer.Core;
using GongSiOrganizer.RadialPrototype.Services;
using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;
using FontFamily = System.Windows.Media.FontFamily;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace GongSiOrganizer.RadialPrototype.Radial;

internal sealed class RadialMenuControl : FrameworkElement
{
    private static readonly DependencyProperty RingTransitionProgressProperty =
        DependencyProperty.Register(
            nameof(RingTransitionProgress),
            typeof(double),
            typeof(RadialMenuControl),
            new FrameworkPropertyMetadata(
                1d,
                FrameworkPropertyMetadataOptions.AffectsRender));
    private static readonly DependencyProperty HoverTransitionProgressProperty =
        DependencyProperty.Register(
            nameof(HoverTransitionProgress),
            typeof(double),
            typeof(RadialMenuControl),
            new FrameworkPropertyMetadata(
                1d,
                FrameworkPropertyMetadataOptions.AffectsRender));
    private static readonly DependencyProperty ScrollHintProgressProperty =
        DependencyProperty.Register(
            nameof(ScrollHintProgress),
            typeof(double),
            typeof(RadialMenuControl),
            new FrameworkPropertyMetadata(
                0d,
                FrameworkPropertyMetadataOptions.AffectsRender));
    private static readonly FontFamily UiFont = new("Microsoft YaHei UI");
    private static readonly Typeface LabelTypeface = new(
        UiFont,
        FontStyles.Normal,
        FontWeights.SemiBold,
        FontStretches.Normal);
    private static readonly Typeface RegularTypeface = new(
        UiFont,
        FontStyles.Normal,
        FontWeights.Normal,
        FontStretches.Normal);

    private readonly DispatcherTimer _hoverTimer;
    private readonly ImageSource? _folderIcon;
    private RadialLayoutPlan? _layout;
    private RadialMenuSession? _session;
    private RadialHit _visualHover = RadialHit.None;
    private RadialHit _previousVisualHover = RadialHit.None;
    private RadialHit _hoverCandidate = RadialHit.None;
    private RadialRingView[]? _previousViews;
    private RadialRingView[]? _pendingAsyncViews;
    private Point _lastPointer;
    private Point _drillAnchor;
    private bool _suppressDeepDrill;
    private bool _wheelTransitionActive;
    private bool _ringTransitionContentOnly;
    private int _ringTransitionDirection = 1;
    private int _wheelTransitionRingIndex = -1;
    private int _queuedWheelSteps;
    private int _queuedWheelRingIndex = -1;
    private int _queuedWheelSlot;
    private int _ringTransitionGeneration;
    private int _hoverTransitionGeneration;
    private int _sourceItemCount = 1;

    private double RingTransitionProgress
    {
        get => (double)GetValue(RingTransitionProgressProperty);
        set => SetValue(RingTransitionProgressProperty, value);
    }

    private double HoverTransitionProgress
    {
        get => (double)GetValue(HoverTransitionProgressProperty);
        set => SetValue(HoverTransitionProgressProperty, value);
    }

    private double ScrollHintProgress
    {
        get => (double)GetValue(ScrollHintProgressProperty);
        set => SetValue(ScrollHintProgressProperty, value);
    }

    public RadialMenuControl()
    {
        Focusable = true;
        FocusVisualStyle = null;
        Cursor = Cursors.Arrow;
        _folderIcon = ShellIconProvider.LoadFolderIcon();
        _hoverTimer = new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = RadialInteractionPolicy.NormalHoverDelay
        };
        _hoverTimer.Tick += OnHoverTimer;
        IsVisibleChanged += (_, _) => UpdateScrollHintAnimation(IsVisible);
    }

    public event Action<string>? Confirmed;
    public event Action<string>? CreateFolderRequested;
    public event Action? DismissRequested;

    public void Configure(
        RadialLayoutPlan layout,
        RadialMenuSession? session,
        int sourceItemCount = 1)
    {
        if (_session is not null) _session.Changed -= OnSessionChanged;
        _layout = layout;
        _session = session;
        if (_session is not null) _session.Changed += OnSessionChanged;
        _sourceItemCount = Math.Max(0, sourceItemCount);
        BeginAnimation(RingTransitionProgressProperty, null);
        BeginAnimation(HoverTransitionProgressProperty, null);
        RingTransitionProgress = 1;
        HoverTransitionProgress = 1;
        _previousViews = null;
        _pendingAsyncViews = null;
        _wheelTransitionActive = false;
        _ringTransitionContentOnly = false;
        _wheelTransitionRingIndex = -1;
        _queuedWheelSteps = 0;
        _queuedWheelRingIndex = -1;
        _visualHover = RadialHit.None;
        _previousVisualHover = RadialHit.None;
        _hoverCandidate = RadialHit.None;
        _suppressDeepDrill = false;
        _hoverTimer.Stop();
        if (IsVisible) UpdateScrollHintAnimation(true);
        InvalidateVisual();
    }

    public void ReleaseSession()
    {
        if (_session is not null) _session.Changed -= OnSessionChanged;
        _session = null;
        _pendingAsyncViews = null;
        _previousViews = null;
        _hoverTimer.Stop();
    }

    public bool NavigateBack()
    {
        if (_session is null) return false;
        var previousViews = CaptureRingViews();
        if (!_session.NavigateBack()) return false;

        _hoverTimer.Stop();
        _hoverCandidate = RadialHit.None;
        _suppressDeepDrill = false;
        StartHoverTransition(RadialHit.None);
        StartRingTransition(previousViews, -1);
        return true;
    }

    public bool RotateDemo(int direction)
    {
        if (_session is null) return false;
        var views = CaptureRingViews();
        var ring = GetActiveScrollableRing(views);
        if (ring < 0) return false;
        return RequestWheelRotation(ring, direction, GetSelectedSlot(views[ring]));
    }

    public bool HoverDemo(int ringIndex)
    {
        if (_session is null || ringIndex is < 0 or > 2) return false;
        var view = _session.GetRingView(ringIndex);
        if (view.TotalCount == 0) return false;
        StartHoverTransition(new RadialHit(ringIndex, 0, false));
        return true;
    }

    internal bool HasMenuHit(Point point) =>
        _layout is not null && HitTest(point).HasValue;

    protected override Size MeasureOverride(Size availableSize) =>
        new(
            double.IsFinite(availableSize.Width) ? availableSize.Width : 1,
            double.IsFinite(availableSize.Height) ? availableSize.Height : 1);

    protected override HitTestResult HitTestCore(PointHitTestParameters hitTestParameters) =>
        new PointHitTestResult(this, hitTestParameters.HitPoint);

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (_layout is null) return;

        if (!_layout.IsInvalidTarget && _session is not null)
            DrawRings(drawingContext, _layout, _session);
        DrawCenter(drawingContext, _layout, _session);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_layout is null) return;
        var point = e.GetPosition(this);
        _lastPointer = point;
        var suppressionReleased = false;
        if (_suppressDeepDrill && Distance(point, _drillAnchor) >= 12)
        {
            _suppressDeepDrill = false;
            suppressionReleased = true;
        }

        var hit = HitTest(point);
        if (!Equals(hit, _visualHover))
            StartHoverTransition(hit);

        if (!Equals(hit, _hoverCandidate) || suppressionReleased)
        {
            ArmHoverIntent(
                hit,
                RadialInteractionPolicy.NormalHoverDelay);
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _hoverTimer.Stop();
        _hoverCandidate = RadialHit.None;
        StartHoverTransition(RadialHit.None);
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_session is null) return;
        var pointer = e.GetPosition(this);
        _lastPointer = pointer;
        var hit = HitTest(pointer);
        if (!Equals(hit, _visualHover)) StartHoverTransition(hit);

        var views = CaptureRingViews();
        var ring = RadialInteractionPolicy.ResolveScrollableRing(hit, views);
        var slot = hit.SlotIndex;
        if (ring < 0)
        {
            // The wheel belongs exclusively to the ring under the pointer.  In
            // particular, never fall back to a scrollable parent when the
            // hovered child ring has four or fewer entries.
            _suppressDeepDrill = false;
            ArmHoverIntent(
                hit,
                RadialInteractionPolicy.WheelHoverDelay);
            e.Handled = true;
            return;
        }

        var direction = e.Delta > 0 ? -1 : 1;
        if (RequestWheelRotation(ring, direction, slot))
        {
            _suppressDeepDrill = false;
            ArmHoverIntent(
                hit,
                RadialInteractionPolicy.WheelHoverDelay,
                false);
            e.Handled = true;
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        if (_layout is null) return;
        if (_layout.IsInvalidTarget)
        {
            DismissRequested?.Invoke();
            e.Handled = true;
            return;
        }

        if (_session is null) return;
        var point = e.GetPosition(this);
        var hit = HitTest(point);
        FolderMenuNode? destination = null;
        if (hit.IsCenter)
        {
            destination = _session.CurrentDestination;
        }
        else if (hit.RingIndex >= 0)
        {
            destination = _session.GetFolder(hit.RingIndex, hit.SlotIndex);
            if (destination is not null) _session.Hover(hit.RingIndex, hit.SlotIndex);
        }

        var action = RadialInteractionPolicy.ResolvePrimaryClick(
            destination is not null,
            (Keyboard.Modifiers & ModifierKeys.Control) != 0);
        switch (action)
        {
            case RadialPrimaryClickAction.Dismiss:
                DismissRequested?.Invoke();
                break;
            case RadialPrimaryClickAction.CreateFolder:
                CreateFolderRequested?.Invoke(destination!.RelativePath);
                break;
            case RadialPrimaryClickAction.Confirm:
                Confirmed?.Invoke(destination!.RelativePath);
                break;
        }

        e.Handled = true;
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);
        Focus();
        if (_layout?.IsInvalidTarget != false || !NavigateBack())
            DismissRequested?.Invoke();
        e.Handled = true;
    }

    private void OnHoverTimer(object? sender, EventArgs e)
    {
        ActivateHoverCandidate();
    }

    private void ActivateHoverCandidate()
    {
        _hoverTimer.Stop();
        if (_session is null || _hoverCandidate.RingIndex < 0) return;
        if (_wheelTransitionActive)
        {
            _hoverTimer.Interval = TimeSpan.FromMilliseconds(90);
            _hoverTimer.Start();
            return;
        }

        if (_suppressDeepDrill && _hoverCandidate.RingIndex == 2) return;

        var previousViews = CaptureRingViews();
        if (_session.Hover(_hoverCandidate.RingIndex, _hoverCandidate.SlotIndex))
        {
            if (_hoverCandidate.RingIndex == 2)
            {
                _suppressDeepDrill = true;
                _drillAnchor = _lastPointer;
            }

            var currentViews = CaptureRingViews();
            if (ShouldAnimateExpansion(previousViews, currentViews))
                StartRingTransition(
                    previousViews,
                    1,
                    contentChangesOnly: true);
            else
                InvalidateVisual();
        }
    }

    private void DrawRings(
        DrawingContext drawingContext,
        RadialLayoutPlan layout,
        RadialMenuSession session)
    {
        var currentViews = Enumerable.Range(0, layout.Rings.Count)
            .Select(session.GetRingView)
            .ToArray();
        var progress = Math.Clamp(RingTransitionProgress, 0, 1);
        for (var ringIndex = 0; ringIndex < layout.Rings.Count; ringIndex++)
        {
            var current = currentViews[ringIndex];
            var previous = _previousViews is not null && ringIndex < _previousViews.Length
                ? _previousViews[ringIndex]
                : null;
            var hasTransitionDifference = previous is not null &&
                                          (_ringTransitionContentOnly
                                              ? !RingContentsEquivalent(previous, current)
                                              : !RingViewsEquivalent(previous, current));
            if (previous is not null &&
                progress < 0.999 &&
                hasTransitionDifference)
            {
                if (ringIndex == _wheelTransitionRingIndex &&
                    previous.TotalCount > RadialLayout.SlotsPerRing &&
                    current.TotalCount > RadialLayout.SlotsPerRing)
                {
                    DrawWheelRingTransition(
                        drawingContext,
                        layout,
                        ringIndex,
                        previous,
                        current,
                        progress);
                }
                else
                {
                    var startScale = _ringTransitionDirection > 0 ? 0.975 : 1.018;
                    DrawRingView(
                        drawingContext,
                        layout,
                        ringIndex,
                        current,
                        1,
                        startScale + (1 - startScale) * progress,
                        0,
                        true);
                }
            }
            else
            {
                DrawRingView(
                    drawingContext,
                    layout,
                    ringIndex,
                    current,
                    1,
                    1,
                    0,
                    true);
            }
        }

        var hoveredScrollableRing = RadialInteractionPolicy.ResolveScrollableRing(
            _visualHover,
            currentViews);
        if (hoveredScrollableRing >= 0)
            DrawScrollMarkers(drawingContext, layout, hoveredScrollableRing);
    }

    private void DrawWheelRingTransition(
        DrawingContext drawingContext,
        RadialLayoutPlan layout,
        int ringIndex,
        RadialRingView previous,
        RadialRingView current,
        double progress)
    {
        var ring = layout.Rings[ringIndex];
        var slotSweep = layout.SweepAngle / RadialLayout.SlotsPerRing;
        var currentOffset = _ringTransitionDirection * slotSweep * (1 - progress);
        var outgoingOffset = -_ringTransitionDirection * slotSweep * progress;
        var outgoingSlot = _ringTransitionDirection > 0
            ? 0
            : RadialLayout.SlotsPerRing - 1;

        drawingContext.PushClip(CreateRingClip(layout, ring));
        DrawRingView(
            drawingContext,
            layout,
            ringIndex,
            current,
            1,
            1,
            currentOffset,
            true);

        var outgoingFolder = previous.Slots[outgoingSlot];
        if (outgoingFolder is not null)
        {
            DrawCell(
                drawingContext,
                layout,
                ring,
                ringIndex,
                outgoingSlot,
                RadialLayout.SlotsPerRing,
                outgoingFolder,
                0,
                string.Equals(
                    outgoingFolder.RelativePath,
                    previous.SelectedRelativePath,
                    StringComparison.OrdinalIgnoreCase),
                outgoingOffset);
        }

        drawingContext.Pop();
    }

    private void DrawRingView(
        DrawingContext drawingContext,
        RadialLayoutPlan layout,
        int ringIndex,
        RadialRingView view,
        double opacity,
        double scale,
        double angleOffset,
        bool interactive)
    {
        if (view.TotalCount == 0 || opacity <= 0.001) return;
        var visibleCount = Math.Min(view.TotalCount, RadialLayout.SlotsPerRing);
        drawingContext.PushOpacity(Math.Clamp(opacity, 0, 1));
        drawingContext.PushTransform(new ScaleTransform(
            scale,
            scale,
            layout.LocalCenter.X,
            layout.LocalCenter.Y));
        for (var slot = 0; slot < visibleCount; slot++)
        {
            var folder = view.Slots[slot];
            var hoverAmount = interactive ? GetHoverAmount(ringIndex, slot) : 0;
            var isSelected = folder is not null &&
                             string.Equals(
                                 folder.RelativePath,
                                 view.SelectedRelativePath,
                                 StringComparison.OrdinalIgnoreCase);
            DrawCell(
                drawingContext,
                layout,
                layout.Rings[ringIndex],
                ringIndex,
                slot,
                visibleCount,
                folder,
                hoverAmount,
                isSelected,
                angleOffset);
        }

        drawingContext.Pop();
        drawingContext.Pop();
    }

    private void DrawScrollMarkers(
        DrawingContext drawingContext,
        RadialLayoutPlan layout,
        int ringIndex)
    {
        var progress = Math.Clamp(ScrollHintProgress, 0, 1);
        var center = layout.LocalCenter;
        var markers = RadialInteractionPolicy.GetScrollMarkers(layout, ringIndex);
        var haloPen = new Pen(
            new SolidColorBrush(Color.FromArgb(180, 245, 250, 255)),
            3.8)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        var arrowPen = new Pen(
            new SolidColorBrush(Color.FromArgb(250, 49, 132, 226)),
            2)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };

        DrawScrollArrow(
            drawingContext,
            center,
            markers.First.Radius,
            markers.First.Angle,
            markers.First.Direction,
            progress,
            haloPen,
            arrowPen);
        DrawScrollArrow(
            drawingContext,
            center,
            markers.Second.Radius,
            markers.Second.Angle,
            markers.Second.Direction,
            progress,
            haloPen,
            arrowPen);
    }

    private static void DrawScrollArrow(
        DrawingContext drawingContext,
        RadialPoint center,
        double markerRadius,
        double anchorAngle,
        int direction,
        double progress,
        Pen haloPen,
        Pen arrowPen)
    {
        var radians = anchorAngle * Math.PI / 180;
        var radialX = Math.Cos(radians);
        var radialY = Math.Sin(radians);
        var tangentX = -radialY * direction;
        var tangentY = radialX * direction;
        var travel = 1.7 * progress;
        var markerCenter = new Point(
            center.X + radialX * markerRadius + tangentX * travel,
            center.Y + radialY * markerRadius + tangentY * travel);
        var arrow = CreateOpenChevron(markerCenter, tangentX, tangentY);
        drawingContext.DrawGeometry(null, haloPen, arrow);
        drawingContext.DrawGeometry(null, arrowPen, arrow);
    }

    private void DrawCell(
        DrawingContext drawingContext,
        RadialLayoutPlan layout,
        RadialRing ring,
        int ringIndex,
        int slot,
        int visibleCount,
        FolderMenuNode? folder,
        double hoverAmount,
        bool isSelected,
        double angleOffset)
    {
        var slotSweep = layout.SweepAngle / visibleCount;
        var angularGap = Math.Min(0.45, slotSweep * 0.025);
        var start = layout.StartAngle + slot * slotSweep + angularGap + angleOffset;
        var end = layout.StartAngle + (slot + 1) * slotSweep - angularGap + angleOffset;
        var inner = ring.InnerRadius + 1.5;
        var outer = ring.OuterRadius - 1.5;
        var midAngle = (start + end) / 2;
        hoverAmount = Math.Clamp(hoverAmount, 0, 1);
        var radians = midAngle * Math.PI / 180;

        var geometry = CreateWedge(layout.LocalCenter, inner, outer, start, end);
        drawingContext.PushTransform(new TranslateTransform(0, 2));
        drawingContext.DrawGeometry(
            new SolidColorBrush(Color.FromArgb(18, 24, 39, 56)),
            null,
            geometry);
        drawingContext.Pop();
        var baseFill = folder is null
            ? Color.FromArgb(36, 242, 247, 252)
            : isSelected
                ? Color.FromArgb(222, 229, 240, 252)
                : Color.FromArgb(216, 242, 246, 251);
        var fill = new SolidColorBrush(Blend(
            baseFill,
            Color.FromArgb(238, 218, 236, 255),
            hoverAmount));
        var baseStroke = isSelected
            ? Color.FromArgb(150, 102, 164, 228)
            : Color.FromArgb(195, 255, 255, 255);
        var stroke = new Pen(
            new SolidColorBrush(Blend(
                baseStroke,
                Color.FromArgb(230, 72, 150, 235),
                hoverAmount)),
            (isSelected ? 1.05 : 0.95) + (isSelected ? 0.65 : 0.8) * hoverAmount);
        stroke.LineJoin = PenLineJoin.Round;
        drawingContext.DrawGeometry(fill, stroke, geometry);

        if (folder is not null)
        {
            var radius = (inner + outer) / 2;
            var center = Polar(layout.LocalCenter, radius, midAngle);
            if (_folderIcon is not null)
                drawingContext.DrawImage(_folderIcon, new Rect(center.X - 7.5, center.Y - 19, 15, 15));

            var arcWidth = Math.Max(44, radius * (end - start) * Math.PI / 180 - 10);
            var radialBandWidth = outer - inner - 12;
            var radialProjection = Math.Abs(Math.Cos(radians));
            var radialTextLimit = radialProjection < 0.18
                ? 132
                : radialBandWidth / radialProjection;
            var labelWidth = Math.Max(42, Math.Min(arcWidth, Math.Min(132, radialTextLimit)));
            DrawCenteredText(
                drawingContext,
                folder.DisplayName,
                center.X,
                center.Y - 1.5,
                labelWidth,
                10.1,
                LabelTypeface,
                Color.FromRgb(37, 47, 58),
                15.5,
                true);
        }

    }

    private void DrawCenter(
        DrawingContext drawingContext,
        RadialLayoutPlan layout,
        RadialMenuSession? session)
    {
        var center = new Point(layout.LocalCenter.X, layout.LocalCenter.Y);
        var radius = layout.CenterRadius;
        drawingContext.DrawEllipse(
            new SolidColorBrush(Color.FromArgb(38, 16, 35, 56)),
            null,
            new Point(center.X, center.Y + 5),
            radius + 2,
            radius + 2);
        drawingContext.DrawEllipse(
            new SolidColorBrush(Color.FromArgb(218, 247, 249, 252)),
            new Pen(new SolidColorBrush(Color.FromArgb(225, 255, 255, 255)), 1.5),
            center,
            radius,
            radius);

        if (layout.IsInvalidTarget)
        {
            DrawCenteredText(
                drawingContext,
                "请在\n文件或文件夹上\n点击鼠标中键",
                center.X,
                center.Y - 22,
                radius * 1.58,
                8.6,
                LabelTypeface,
                Color.FromRgb(44, 53, 63),
                13.2,
                false);
            return;
        }

        const double footerTopOffset = 24;
        var footerGeometry = CreateCenterFooter(center, radius, footerTopOffset);
        drawingContext.DrawGeometry(
            new SolidColorBrush(Color.FromArgb(82, 221, 232, 244)),
            null,
            footerGeometry);
        var footerHalfWidth = Math.Sqrt(radius * radius - footerTopOffset * footerTopOffset);
        drawingContext.DrawLine(
            new Pen(new SolidColorBrush(Color.FromArgb(105, 148, 166, 188)), 0.8),
            new Point(center.X - footerHalfWidth, center.Y + footerTopOffset),
            new Point(center.X + footerHalfWidth, center.Y + footerTopOffset));
        drawingContext.DrawLine(
            new Pen(new SolidColorBrush(Color.FromArgb(70, 137, 155, 177)), 0.75),
            new Point(center.X, center.Y + footerTopOffset + 8),
            new Point(center.X, center.Y + radius - 9));

        var destinationName = session?.CurrentDestination?.DisplayName ?? "请选择目录";
        var actionText = session?.IsCurrentChildrenLoading == true
            ? "正在读取下一级…"
            : !string.IsNullOrWhiteSpace(session?.CurrentChildrenError)
                ? "目录暂时无法读取"
                : session?.CurrentDestination is null
                    ? _sourceItemCount > 1
                        ? $"已选择 {_sourceItemCount} 项"
                        : "悬停选择后确认"
                    : _sourceItemCount > 1
                        ? $"左键移动 {_sourceItemCount} 项"
                        : "左键确认移动";
        DrawCenteredText(
            drawingContext,
            "目标文件夹",
            center.X,
            center.Y - 45,
            radius * 1.26,
            7.1,
            RegularTypeface,
            Color.FromRgb(104, 119, 137),
            10,
            false);
        if (_folderIcon is not null)
            drawingContext.DrawImage(
                _folderIcon,
                new Rect(center.X - 7.5, center.Y - 32, 15, 15));
        DrawCenteredText(
            drawingContext,
            destinationName,
            center.X,
            center.Y - 13,
            radius * 1.34,
            9.6,
            LabelTypeface,
            Color.FromRgb(34, 46, 59),
            13.5,
            true);
        DrawCenteredText(
            drawingContext,
            actionText,
            center.X,
            center.Y + 4,
            radius * 1.34,
            7.7,
            LabelTypeface,
            Color.FromRgb(79, 118, 158),
            11,
            false);
        DrawCenteredText(
            drawingContext,
            "右键  返回",
            center.X - radius * 0.32,
            center.Y + 34,
            radius * 0.60,
            6.9,
            RegularTypeface,
            Color.FromRgb(91, 107, 125),
            9.5,
            false);
        DrawCenteredText(
            drawingContext,
            "Esc  取消",
            center.X + radius * 0.32,
            center.Y + 34,
            radius * 0.60,
            6.9,
            RegularTypeface,
            Color.FromRgb(91, 107, 125),
            9.5,
            false);
    }

    private void DrawCenteredText(
        DrawingContext drawingContext,
        string text,
        double centerX,
        double top,
        double maxWidth,
        double fontSize,
        Typeface typeface,
        Color color,
        double lineHeight,
        bool trim)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            System.Windows.FlowDirection.LeftToRight,
            typeface,
            fontSize,
            new SolidColorBrush(color),
            VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            TextAlignment = TextAlignment.Center,
            MaxTextWidth = maxWidth,
            LineHeight = lineHeight,
            Trimming = trim ? TextTrimming.CharacterEllipsis : TextTrimming.None,
            MaxLineCount = Math.Max(1, text.Count(character => character == '\n') + 1)
        };
        drawingContext.DrawText(formatted, new Point(centerX - maxWidth / 2, top));
    }

    private static StreamGeometry CreateCenterFooter(
        Point center,
        double radius,
        double topOffset)
    {
        var halfWidth = Math.Sqrt(radius * radius - topOffset * topOffset);
        var left = new Point(center.X - halfWidth, center.Y + topOffset);
        var right = new Point(center.X + halfWidth, center.Y + topOffset);
        var geometry = new StreamGeometry { FillRule = FillRule.Nonzero };
        using var context = geometry.Open();
        context.BeginFigure(left, true, true);
        context.LineTo(right, true, true);
        context.ArcTo(
            left,
            new Size(radius, radius),
            0,
            false,
            SweepDirection.Clockwise,
            true,
            true);
        geometry.Freeze();
        return geometry;
    }

    private static StreamGeometry CreateOpenChevron(
        Point center,
        double directionX,
        double directionY)
    {
        const double halfLength = 3;
        const double halfWidth = 3.8;
        var normalX = -directionY;
        var normalY = directionX;
        var tip = new Point(
            center.X + directionX * halfLength,
            center.Y + directionY * halfLength);
        var tail = new Point(
            center.X - directionX * halfLength,
            center.Y - directionY * halfLength);
        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        context.BeginFigure(
            new Point(
                tail.X + normalX * halfWidth,
                tail.Y + normalY * halfWidth),
            false,
            false);
        context.LineTo(tip, true, true);
        context.LineTo(
            new Point(
                tail.X - normalX * halfWidth,
                tail.Y - normalY * halfWidth),
            true,
            true);
        geometry.Freeze();
        return geometry;
    }

    private static Geometry CreateRingClip(RadialLayoutPlan layout, RadialRing ring)
    {
        var innerRadius = Math.Max(1, ring.InnerRadius - 4);
        var outerRadius = ring.OuterRadius + 5;
        if (layout.SweepAngle >= 359.5)
        {
            var group = new GeometryGroup { FillRule = FillRule.EvenOdd };
            group.Children.Add(new EllipseGeometry(
                new Point(layout.LocalCenter.X, layout.LocalCenter.Y),
                outerRadius,
                outerRadius));
            group.Children.Add(new EllipseGeometry(
                new Point(layout.LocalCenter.X, layout.LocalCenter.Y),
                innerRadius,
                innerRadius));
            group.Freeze();
            return group;
        }

        var startAngle = layout.StartAngle;
        var endAngle = layout.StartAngle + layout.SweepAngle;
        var geometry = new StreamGeometry { FillRule = FillRule.EvenOdd };
        using var context = geometry.Open();
        context.BeginFigure(
            Polar(layout.LocalCenter, innerRadius, startAngle),
            true,
            true);
        context.LineTo(
            Polar(layout.LocalCenter, outerRadius, startAngle),
            true,
            true);
        context.ArcTo(
            Polar(layout.LocalCenter, outerRadius, endAngle),
            new Size(outerRadius, outerRadius),
            0,
            layout.SweepAngle > 180,
            SweepDirection.Clockwise,
            true,
            true);
        context.LineTo(
            Polar(layout.LocalCenter, innerRadius, endAngle),
            true,
            true);
        context.ArcTo(
            Polar(layout.LocalCenter, innerRadius, startAngle),
            new Size(innerRadius, innerRadius),
            0,
            layout.SweepAngle > 180,
            SweepDirection.Counterclockwise,
            true,
            true);
        geometry.Freeze();
        return geometry;
    }

    private int GetActiveScrollableRing(IReadOnlyList<RadialRingView> views)
    {
        var hoveredRing = RadialInteractionPolicy.ResolveScrollableRing(_visualHover, views);
        if (hoveredRing >= 0) return hoveredRing;

        for (var ringIndex = views.Count - 1; ringIndex >= 0; ringIndex--)
        {
            var view = views[ringIndex];
            if (view.TotalCount > RadialLayout.SlotsPerRing)
                return ringIndex;
        }

        return -1;
    }

    private bool RequestWheelRotation(int ring, int direction, int slot)
    {
        if (_session is null || ring is < 0 or > 2) return false;
        direction = Math.Sign(direction);
        if (direction == 0) return false;

        if (_wheelTransitionActive)
        {
            if (_queuedWheelSteps == 0 || _queuedWheelRingIndex == ring)
                _queuedWheelSteps = Math.Clamp(_queuedWheelSteps + direction, -3, 3);
            else
                _queuedWheelSteps = direction;
            _queuedWheelRingIndex = ring;
            _queuedWheelSlot = Math.Clamp(slot, 0, RadialLayout.SlotsPerRing - 1);
            return true;
        }

        var previousViews = CaptureRingViews();
        if (!_session.RotateAndPreview(ring, direction, slot)) return false;
        StartRingTransition(previousViews, direction, ring);
        return true;
    }

    private void StartQueuedWheelRotation()
    {
        if (_session is null || _queuedWheelSteps == 0) return;
        var direction = Math.Sign(_queuedWheelSteps);
        var ring = _queuedWheelRingIndex;
        var slot = _queuedWheelSlot;
        _queuedWheelSteps -= direction;
        if (_queuedWheelSteps == 0) _queuedWheelRingIndex = -1;
        _ = RequestWheelRotation(ring, direction, slot);
    }

    private static int GetSelectedSlot(RadialRingView view)
    {
        if (view.SelectedRelativePath is null) return 0;
        for (var slot = 0; slot < view.Slots.Count; slot++)
        {
            if (string.Equals(
                    view.Slots[slot]?.RelativePath,
                    view.SelectedRelativePath,
                    StringComparison.OrdinalIgnoreCase))
                return slot;
        }

        return 0;
    }

    private static StreamGeometry CreateWedge(
        RadialPoint center,
        double innerRadius,
        double outerRadius,
        double startAngle,
        double endAngle)
    {
        var geometry = new StreamGeometry { FillRule = FillRule.EvenOdd };
        using var context = geometry.Open();
        var sweep = endAngle - startAngle;
        var corner = Math.Min(6, (outerRadius - innerRadius) / 4);
        var maximumCornerAngle = sweep * 0.22;
        var innerCornerAngle = Math.Min(
            maximumCornerAngle,
            corner / innerRadius * 180 / Math.PI);
        var outerCornerAngle = Math.Min(
            maximumCornerAngle,
            corner / outerRadius * 180 / Math.PI);
        var innerStartCorner = Polar(center, innerRadius, startAngle);
        var outerStartCorner = Polar(center, outerRadius, startAngle);
        var outerEndCorner = Polar(center, outerRadius, endAngle);
        var innerEndCorner = Polar(center, innerRadius, endAngle);
        var innerArcStart = Polar(center, innerRadius, startAngle + innerCornerAngle);
        var startEdgeInner = Polar(center, innerRadius + corner, startAngle);
        var startEdgeOuter = Polar(center, outerRadius - corner, startAngle);
        var outerArcStart = Polar(center, outerRadius, startAngle + outerCornerAngle);
        var outerArcEnd = Polar(center, outerRadius, endAngle - outerCornerAngle);
        var endEdgeOuter = Polar(center, outerRadius - corner, endAngle);
        var endEdgeInner = Polar(center, innerRadius + corner, endAngle);
        var innerArcEnd = Polar(center, innerRadius, endAngle - innerCornerAngle);
        var outerArcSweep = sweep - outerCornerAngle * 2;
        var innerArcSweep = sweep - innerCornerAngle * 2;

        context.BeginFigure(innerArcStart, true, true);
        context.QuadraticBezierTo(innerStartCorner, startEdgeInner, true, true);
        context.LineTo(startEdgeOuter, true, true);
        context.QuadraticBezierTo(outerStartCorner, outerArcStart, true, true);
        context.ArcTo(
            outerArcEnd,
            new Size(outerRadius, outerRadius),
            0,
            outerArcSweep > 180,
            SweepDirection.Clockwise,
            true,
            true);
        context.QuadraticBezierTo(outerEndCorner, endEdgeOuter, true, true);
        context.LineTo(endEdgeInner, true, true);
        context.QuadraticBezierTo(innerEndCorner, innerArcEnd, true, true);
        context.ArcTo(
            innerArcStart,
            new Size(innerRadius, innerRadius),
            0,
            innerArcSweep > 180,
            SweepDirection.Counterclockwise,
            true,
            true);
        geometry.Freeze();
        return geometry;
    }

    private static Point Polar(RadialPoint center, double radius, double angle)
    {
        var radians = angle * Math.PI / 180;
        return new Point(
            center.X + radius * Math.Cos(radians),
            center.Y + radius * Math.Sin(radians));
    }

    private RadialRingView[] CaptureRingViews()
    {
        if (_session is null) return [];
        return Enumerable.Range(0, 3)
            .Select(_session.GetRingView)
            .ToArray();
    }

    private void UpdateScrollHintAnimation(bool active)
    {
        BeginAnimation(ScrollHintProgressProperty, null);
        ScrollHintProgress = 0;
        if (!active) return;

        BeginAnimation(
            ScrollHintProgressProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(720))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            },
            HandoffBehavior.SnapshotAndReplace);
    }

    private void StartRingTransition(
        RadialRingView[] previousViews,
        int direction,
        int wheelRingIndex = -1,
        bool contentChangesOnly = false)
    {
        var currentViews = CaptureRingViews();
        if (previousViews.Length == currentViews.Length &&
            previousViews.Zip(
                    currentViews,
                    (previous, current) => contentChangesOnly
                        ? RingContentsEquivalent(previous, current)
                        : RingViewsEquivalent(previous, current))
                .All(equal => equal))
        {
            InvalidateVisual();
            return;
        }

        _previousViews = previousViews;
        _ringTransitionDirection = direction == 0 ? 1 : Math.Sign(direction);
        _wheelTransitionRingIndex = wheelRingIndex;
        _wheelTransitionActive = wheelRingIndex >= 0;
        _ringTransitionContentOnly = contentChangesOnly;
        if (!_wheelTransitionActive)
        {
            _queuedWheelSteps = 0;
            _queuedWheelRingIndex = -1;
        }
        var generation = ++_ringTransitionGeneration;
        BeginAnimation(RingTransitionProgressProperty, null);
        RingTransitionProgress = 1;
        var duration = TimeSpan.FromMilliseconds(wheelRingIndex >= 0 ? 290 : 210);
        var animation = new DoubleAnimationUsingKeyFrames
        {
            Duration = new Duration(duration),
            FillBehavior = FillBehavior.Stop
        };
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(
            0,
            KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new SplineDoubleKeyFrame(
            1,
            KeyTime.FromTimeSpan(duration),
            new KeySpline(0.2, 0.72, 0.2, 1)));
        animation.Completed += (_, _) =>
        {
            if (generation != _ringTransitionGeneration) return;
            _previousViews = null;
            _wheelTransitionRingIndex = -1;
            _ringTransitionContentOnly = false;
            var continueWheel = _wheelTransitionActive && _queuedWheelSteps != 0;
            _wheelTransitionActive = false;
            InvalidateVisual();
            if (continueWheel) StartQueuedWheelRotation();
        };
        BeginAnimation(
            RingTransitionProgressProperty,
            animation,
            HandoffBehavior.SnapshotAndReplace);
    }

    private void StartHoverTransition(RadialHit next)
    {
        if (Equals(next, _visualHover)) return;
        _previousVisualHover = _visualHover;
        _visualHover = next;
        var generation = ++_hoverTransitionGeneration;
        BeginAnimation(HoverTransitionProgressProperty, null);
        HoverTransitionProgress = 1;
        var animation = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(130))
        {
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            FillBehavior = FillBehavior.Stop
        };
        animation.Completed += (_, _) =>
        {
            if (generation != _hoverTransitionGeneration) return;
            _previousVisualHover = RadialHit.None;
            InvalidateVisual();
        };
        BeginAnimation(
            HoverTransitionProgressProperty,
            animation,
            HandoffBehavior.SnapshotAndReplace);
    }

    private double GetHoverAmount(int ringIndex, int slotIndex)
    {
        var progress = Math.Clamp(HoverTransitionProgress, 0, 1);
        var current = _visualHover.RingIndex == ringIndex &&
                      _visualHover.SlotIndex == slotIndex;
        var previous = _previousVisualHover.RingIndex == ringIndex &&
                       _previousVisualHover.SlotIndex == slotIndex;
        if (current && previous) return 1;
        return Math.Clamp(
            (current ? progress : 0) + (previous ? 1 - progress : 0),
            0,
            1);
    }

    private static bool RingViewsEquivalent(RadialRingView left, RadialRingView right)
    {
        if (!RingContentsEquivalent(left, right) ||
            !string.Equals(
                left.SelectedRelativePath,
                right.SelectedRelativePath,
                StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    private static bool RingContentsEquivalent(RadialRingView left, RadialRingView right)
    {
        if (left.TotalCount != right.TotalCount ||
            left.Offset != right.Offset ||
            left.Slots.Count != right.Slots.Count)
            return false;

        for (var index = 0; index < left.Slots.Count; index++)
        {
            if (!string.Equals(
                    left.Slots[index]?.RelativePath,
                    right.Slots[index]?.RelativePath,
                    StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    internal static bool ShouldAnimateExpansion(
        IReadOnlyList<RadialRingView> previousViews,
        IReadOnlyList<RadialRingView> currentViews)
    {
        var count = Math.Min(previousViews.Count, currentViews.Count);
        // Ring 0 is the current level.  Merely selecting another item there is
        // not an expansion; only a changed, non-empty child ring should move.
        for (var index = 1; index < count; index++)
        {
            if (currentViews[index].TotalCount > 0 &&
                !RingContentsEquivalent(previousViews[index], currentViews[index]))
                return true;
        }

        return false;
    }

    private static Color Blend(Color from, Color to, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromArgb(
            Lerp(from.A, to.A, amount),
            Lerp(from.R, to.R, amount),
            Lerp(from.G, to.G, amount),
            Lerp(from.B, to.B, amount));
    }

    private static byte Lerp(byte from, byte to, double amount) =>
        (byte)Math.Round(from + (to - from) * amount);

    private RadialHit HitTest(Point point)
    {
        if (_layout is null) return RadialHit.None;
        var first = VisibleSlots(0);
        var second = VisibleSlots(1);
        var third = VisibleSlots(2);
        return RadialLayout.HitTest(
            _layout,
            new RadialPoint(point.X, point.Y),
            first,
            second,
            third);
    }

    private void BeginFolderPrefetch(FolderMenuNode folder)
    {
        if (_session is null) return;
        var previous = CaptureRingViews();
        _pendingAsyncViews = previous;
        if (_session.Prefetch(folder))
        {
            InvalidateVisual();
            return;
        }

        if (ReferenceEquals(_pendingAsyncViews, previous))
            _pendingAsyncViews = null;
    }

    private void ArmHoverIntent(
        RadialHit hit,
        TimeSpan delay,
        bool beginPrefetch = true)
    {
        _hoverTimer.Stop();
        _hoverCandidate = hit;
        var folder = hit.RingIndex >= 0
            ? _session?.GetFolder(hit.RingIndex, hit.SlotIndex)
            : null;
        if (folder is null) return;
        if (beginPrefetch) BeginFolderPrefetch(folder);
        _hoverTimer.Interval = delay;
        _hoverTimer.Start();
    }

    private void OnSessionChanged(object? sender, EventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(
                () => OnSessionChanged(sender, e),
                DispatcherPriority.Input);
            return;
        }

        if (!ReferenceEquals(sender, _session)) return;
        var previous = _pendingAsyncViews;
        _pendingAsyncViews = null;
        if (previous is { Length: > 0 })
            StartRingTransition(
                previous,
                1,
                contentChangesOnly: true);
        else
            InvalidateVisual();
    }

    private int VisibleSlots(int ringIndex) => _session is null
        ? 0
        : Math.Min(_session.GetRingView(ringIndex).TotalCount, RadialLayout.SlotsPerRing);

    private static double Distance(Point left, Point right)
    {
        var dx = left.X - right.X;
        var dy = left.Y - right.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
