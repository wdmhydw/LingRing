using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using GongSiOrganizer.Core;
using GongSiOrganizer.RadialPrototype.Presentation;
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
    private static readonly DependencyProperty EntryBreathProperty = DependencyProperty.Register(
        nameof(EntryBreath), typeof(double), typeof(RadialMenuControl),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    private static readonly DependencyProperty OpenProgressProperty = DependencyProperty.Register(
        nameof(OpenProgress), typeof(double), typeof(RadialMenuControl),
        new FrameworkPropertyMetadata(1d, FrameworkPropertyMetadataOptions.AffectsRender));
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
    private static readonly FontFamily UiFont = new("Microsoft YaHei UI");
    private static readonly Typeface LabelTypeface = RadialTypography.Typeface;
    private static readonly Typeface RegularTypeface = new(
        UiFont,
        FontStyles.Normal,
        FontWeights.Normal,
        FontStretches.Normal);

    private readonly DispatcherTimer _hoverTimer;
    private readonly DispatcherTimer _entryTimer;
    private readonly DispatcherTimer _nameTimer;
    private readonly RadialEntryIntent _entryIntent = new();
    private FolderMenuNode? _entryNode;
    private bool _breathing;
    private readonly Dictionary<(double Inner, double Outer, double Start, double End), Geometry> _geometryCache = [];
    private readonly Dictionary<Geometry, RadialCellContent> _contentCache = [];
    private sealed record RenderedLabel(RadialCellLabel Layout, FormattedText? Text, Point Origin);
    private readonly Dictionary<(string Name, RadialCellContent Content, double PixelsPerDip), RenderedLabel> _labelCache = [];
    private RadialLayoutPlan? _layout;
    private RadialMenuSession? _session;
    private RadialHit _visualHover = RadialHit.None;
    private RadialHit _previousVisualHover = RadialHit.None;
    private RadialHit _hoverCandidate = RadialHit.None;
    private RadialRingView[]? _previousViews;
    private RadialRingView[] _observedViews = [];
    private Point _lastPointer;
    private RadialHit _reentryHit = RadialHit.None;
    private string? _reentryPath;
    private Point _reentryOrigin;
    private int _reentryGeneration;
    private bool _wheelTransitionActive;
    private bool _applyingWheelRotation;
    private bool _ringTransitionContentOnly;
    private int _ringTransitionDirection = 1;
    private int _wheelTransitionRingIndex = -1;
    private int _queuedWheelSteps;
    private int _queuedWheelRingIndex = -1;
    private int _queuedWheelSlot;
    private int _ringTransitionGeneration;
    private int _hoverTransitionGeneration;
    private int _sourceItemCount = 1;
    private double EntryBreath { get => (double)GetValue(EntryBreathProperty); set => SetValue(EntryBreathProperty, value); }
    private double OpenProgress { get => (double)GetValue(OpenProgressProperty); set => SetValue(OpenProgressProperty, value); }
    internal bool IsTransitioning => _previousViews is not null;
    internal bool EntryPending => _entryIntent.IsPending;
    internal bool EntryLocked => _entryIntent.IsLocked;

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

    public RadialMenuControl()
    {
        Focusable = true;
        FocusVisualStyle = null;
        Cursor = Cursors.Arrow;
        _hoverTimer = new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = RadialInteractionPolicy.NormalHoverDelay
        };
        _hoverTimer.Tick += OnHoverTimer;
        _entryTimer = new DispatcherTimer(DispatcherPriority.Input) { Interval = RadialInteractionPolicy.EntryArcDelay };
        _entryTimer.Tick += (_, _) => ActivateEntryArc();
        _nameTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(550) };
        _nameTimer.Tick += (_, _) => ShowNameTip();
        IsVisibleChanged += (_, _) => { UpdateEntryBreathing(); if (!IsVisible) CancelIntent(true); };
    }

    public event Action<string>? Confirmed;
    public event Action<string>? CreateFolderRequested;
    public event Action? DismissRequested;
    public event Action<FolderMenuNode?, Point>? NameTipRequested;
    public event Action? NameTipDismissRequested;

    public void Configure(
        RadialLayoutPlan layout,
        RadialMenuSession? session,
        int sourceItemCount = 1)
    {
        if (_session is not null) { _session.Changed -= OnSessionChanged; if (!ReferenceEquals(_session, session)) _session.Dispose(); }
        _layout = layout;
        _ringTransitionGeneration++;
        _hoverTransitionGeneration++;
        _session = session;
        if (_session is not null) _session.Changed += OnSessionChanged;
        _sourceItemCount = Math.Max(0, sourceItemCount);
        BeginAnimation(RingTransitionProgressProperty, null);
        BeginAnimation(HoverTransitionProgressProperty, null);
        RingTransitionProgress = 1;
        HoverTransitionProgress = 1;
        _previousViews = null;
        _observedViews = CaptureRingViews();
        _wheelTransitionActive = false;
        _ringTransitionContentOnly = false;
        _wheelTransitionRingIndex = -1;
        _queuedWheelSteps = 0;
        _queuedWheelRingIndex = -1;
        _visualHover = RadialHit.None;
        _previousVisualHover = RadialHit.None;
        _hoverCandidate = RadialHit.None;
        CancelIntent(true);
        _contentCache.Clear();
        _labelCache.Clear();
        _geometryCache.Clear();
        _hoverTimer.Stop();
        InvalidateVisual();
        UpdateDetails();
    }

    public void ReleaseSession()
    {
        if (_session is not null) _session.Changed -= OnSessionChanged;
        _session?.Dispose();
        _ringTransitionGeneration++;
        BeginAnimation(RingTransitionProgressProperty, null);
        BeginAnimation(OpenProgressProperty, null);
        CancelIntent(true);
        _session = null;
        _observedViews = [];
        _previousViews = null;
        _hoverTimer.Stop();
        UpdateEntryBreathing();
    }

    public bool NavigateBack()
    {
        if (_session is null) return false;
        var previousViews = CaptureRingViews();
        if (!_session.NavigateBack()) return false;

        _hoverTimer.Stop();
        _hoverCandidate = RadialHit.None;
        CancelIntent(true);
        _entryIntent.Lock();
        StartHoverTransition(RadialHit.None);
        StartRingTransition(previousViews, -1);
        UpdateDetails();
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

    internal bool HasMenuHit(Point point) => _layout is not null &&
        point.X >= 0 && point.X <= _layout.WindowBounds.Width &&
        point.Y >= 0 && point.Y <= _layout.WindowBounds.Height &&
        HitTest(point).HasValue;

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
        DrawEntryArc(drawingContext);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        ProcessPointer(e.GetPosition(this), e.LeftButton == MouseButtonState.Pressed || e.MiddleButton == MouseButtonState.Pressed);
    }

    internal void ProcessPointer(Point point, bool buttonDown = false)
    {
        if (_layout is null || _session is null) return;
        // Physical one-pixel moves are fractional DIPs at 125% / 150% DPI.
        // A per-event one-DIP threshold discarded slow, high-rate pointer input.
        var moved = Distance(point, _lastPointer) > .001;
        _lastPointer = point;
        if (buttonDown) { CancelIntent(true); return; }
        var hit = HitTest(point);
        if (!Equals(hit, _visualHover)) StartHoverTransition(hit);
        if (!Equals(hit, _hoverCandidate)) ArmHoverIntent(hit, RadialInteractionPolicy.NormalHoverDelay);
        var folder = hit.RingIndex >= 0 ? _session.GetFolder(hit.RingIndex, hit.SlotIndex) : null;
        if (folder is null || !RadialInteractionPolicy.RequiresEntryArc(_layout, hit))
        {
            _entryTimer.Stop();
            if (!hit.HasValue && TryRearmFromOutside(point)) return;
            _entryIntent.Cancel(true);
            _reentryPath = null;
            return;
        }
        var local = new RadialPoint(point.X, point.Y);
        if (_reentryPath is not null && folder.RelativePath != _reentryPath) _reentryPath = null;
        if (CanEnter(folder) && RadialInteractionPolicy.IsEntryArc(_layout, hit, local, VisibleSlots(hit.RingIndex)))
        {
            DismissNameTip();
            if (_entryIntent.VisitArc(folder.RelativePath, _ringTransitionGeneration))
            {
                _entryNode = folder;
                // Remember a real exit/re-entry while content animates. The
                // pending gesture is evaluated once the new content settles.
                if (!IsTransitioning) _entryTimer.Start();
                InvalidateVisual();
            }
        }
        else
        {
            _entryTimer.Stop(); _entryIntent.Cancel();
            if (moved && RadialInteractionPolicy.IsReadingBody(_layout, hit, local, VisibleSlots(hit.RingIndex)))
                _entryIntent.VisitBody(folder.RelativePath, _ringTransitionGeneration);
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        var preserveExit = TryRearmFromOutside(e.GetPosition(this));
        CancelIntent(!preserveExit);
        StartHoverTransition(RadialHit.None);
    }

    private bool TryRearmFromOutside(Point point)
    {
        if (_session is null || _reentryPath is null || _reentryGeneration != _ringTransitionGeneration ||
            _session.GetFolder(_reentryHit.RingIndex, _reentryHit.SlotIndex)?.RelativePath != _reentryPath)
            return false;
        var travel = Distance(point, _reentryOrigin);
        // A genuine local exit is enough even on the outside of the painted
        // wheel. Do not interpret redraws under a stationary pointer as exits.
        if (travel <= 2 || travel > 48) return false;
        _entryIntent.VisitBody(_reentryPath, _reentryGeneration);
        return true;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_session is null) return;
        CancelIntent(true);
        _entryIntent.Lock();
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
            ArmHoverIntent(
                hit,
                RadialInteractionPolicy.WheelHoverDelay);
            e.Handled = true;
            return;
        }

        var direction = e.Delta > 0 ? -1 : 1;
        if (RequestWheelRotation(ring, direction, slot))
        {
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
        ProcessPrimaryClick(e.GetPosition(this), (Keyboard.Modifiers & ModifierKeys.Control) != 0);
        e.Handled = true;
    }

    internal void ProcessPrimaryClick(Point point, bool controlPressed = false)
    {
        if (_layout is null) return;
        CancelIntent(true);
        if (IsTransitioning || OpenProgress < .98) return;
        if (_layout.IsInvalidTarget)
        {
            DismissRequested?.Invoke();
            return;
        }

        if (_session is null) return;
        var hit = HitTest(point);
        if (IsProtectedEntryHit(hit, point)) return;
        FolderMenuNode? destination = null;
        if (hit.IsCenter)
        {
            destination = _session.CurrentDestination;
        }
        else if (hit.RingIndex >= 0)
        {
            destination = _session.GetFolder(hit.RingIndex, hit.SlotIndex);
        }

        var action = RadialInteractionPolicy.ResolvePrimaryClick(
            destination is not null,
            controlPressed);
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
        if (IsTransitioning)
        {
            _hoverTimer.Interval = TimeSpan.FromMilliseconds(90);
            _hoverTimer.Start();
            return;
        }

        if (_layout is null || RadialInteractionPolicy.RequiresEntryArc(_layout, _hoverCandidate)) return;

        var previousViews = CaptureRingViews();
        if (_session.Hover(_hoverCandidate.RingIndex, _hoverCandidate.SlotIndex))
        {
            var currentViews = CaptureRingViews();
            if (ShouldAnimateExpansion(previousViews, currentViews))
                StartRingTransition(
                    previousViews,
                    1,
                    contentChangesOnly: true);
            else
                InvalidateVisual();
            UpdateDetails();
        }
    }

    internal static bool CanEnter(FolderMenuNode folder) => folder.Children is AsyncFolderMenuNodeList list
        ? !list.IsLoaded || list.Count > 0 : folder.Children.Count > 0;

    internal bool IsProtectedEntryHit(RadialHit hit, Point point) => _layout is not null &&
        hit.RingIndex >= 0 && _session?.GetFolder(hit.RingIndex, hit.SlotIndex) is { } folder &&
        CanEnter(folder) && RadialInteractionPolicy.IsEntryArc(_layout, hit, new RadialPoint(point.X, point.Y), VisibleSlots(hit.RingIndex));

    internal void CancelIntent(bool forgetBody = false)
    {
        _entryTimer.Stop(); _hoverTimer.Stop(); DismissNameTip();
        _entryIntent.Cancel(forgetBody); _entryNode = null;
        if (forgetBody) { _hoverCandidate = RadialHit.None; _reentryPath = null; }
    }

    private void ActivateEntryArc()
    {
        _entryTimer.Stop();
        if (_session is null || _layout is null || IsTransitioning || _entryNode is null) return;
        var hit = HitTest(_lastPointer);
        var node = hit.RingIndex >= 0 ? _session.GetFolder(hit.RingIndex, hit.SlotIndex) : null;
        if (!ReferenceEquals(node, _entryNode) || !IsProtectedEntryHit(hit, _lastPointer)) return;
        if (!_entryIntent.TryEnter(node!.RelativePath, _ringTransitionGeneration)) return;
        EnterFolder(hit);
    }

    private void EnterFolder(RadialHit hit)
    {
        if (_session is null || IsTransitioning || hit.RingIndex < 0) return;
        var folder = _session.GetFolder(hit.RingIndex, hit.SlotIndex);
        if (folder is null || !CanEnter(folder)) return;
        CancelIntent(true); _entryIntent.Lock();
        var previous = CaptureRingViews();
        if (!_session.Hover(hit.RingIndex, hit.SlotIndex)) return;
        StartHoverTransition(RadialHit.None);
        StartRingTransition(previous, 1, contentChangesOnly: true);
        var underPointer = HitTest(_lastPointer);
        if (underPointer.RingIndex == 2 && _session.GetFolder(2, underPointer.SlotIndex) is { } next)
        {
            _reentryHit = underPointer;
            _reentryPath = next.RelativePath;
            _reentryOrigin = _lastPointer;
            _reentryGeneration = _ringTransitionGeneration;
        }
        UpdateDetails();
    }

    private void UpdateDetails() => InvalidateVisual();

    private void DismissNameTip()
    {
        _nameTimer.Stop();
        NameTipDismissRequested?.Invoke();
    }

    private void ShowNameTip()
    {
        _nameTimer.Stop();
        if (_session is null || _layout is null || !IsVisible || IsTransitioning) return;
        if (_visualHover.IsCenter)
        {
            NameTipRequested?.Invoke(_session.CurrentDestination, _lastPointer);
            return;
        }
        var hit = _visualHover;
        if (hit.RingIndex < 0 || _session.GetFolder(hit.RingIndex, hit.SlotIndex) is not { } folder ||
            IsProtectedEntryHit(hit, _lastPointer)) return;
        var ring = _layout.Rings[hit.RingIndex];
        var sweep = RadialLayout.SlotSweep(ring, VisibleSlots(hit.RingIndex));
        var gap = RadialLayout.CellGap(ring, VisibleSlots(hit.RingIndex));
        var start = RadialLayout.SlotStart(ring, VisibleSlots(hit.RingIndex)) + hit.SlotIndex * sweep + gap;
        var end = start + sweep - 2 * gap;
        var geometry = CellGeometry(_layout, ring.InnerRadius, ring.OuterRadius, start, end);
        var content = CellContent(geometry, Polar(_layout.LocalCenter, (ring.InnerRadius + ring.OuterRadius) / 2, (start + end) / 2));
        if (FormatLabel(folder.DisplayName, content).Layout.IsTruncated)
            NameTipRequested?.Invoke(folder, _lastPointer);
    }

    private Geometry CellGeometry(RadialLayoutPlan layout, double inner, double outer, double start, double end)
    {
        var key = (inner, outer, start, end);
        if (_geometryCache.TryGetValue(key, out var shape)) return shape;
        if (_geometryCache.Count > 256) { _geometryCache.Clear(); _contentCache.Clear(); }
        return _geometryCache[key] = RadialGeometry.Wedge(layout.LocalCenter, inner, outer, start, end);
    }

    private RadialCellContent CellContent(Geometry geometry, Point center)
    {
        if (_contentCache.TryGetValue(geometry, out var content)) return content;
        return _contentCache[geometry] = RadialCellContent.Fit(geometry, center, _layout!.LocalCenter);
    }

    private RenderedLabel FormatLabel(string name, RadialCellContent content)
    {
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var key = (name, content, pixelsPerDip);
        if (_labelCache.TryGetValue(key, out var label)) return label;
        double Measure(string text) => new FormattedText(text, CultureInfo.CurrentUICulture,
            System.Windows.FlowDirection.LeftToRight, LabelTypeface, RadialTypography.FontSize,
            System.Windows.Media.Brushes.Black, pixelsPerDip).BuildGeometry(new Point()).Bounds.Width;
        if (_labelCache.Count > 256) _labelCache.Clear();
        var layout = RadialLabelFormatter.Layout(name, content, Measure);
        if (layout.Bounds.IsEmpty) return _labelCache[key] = new(layout, null, default);
        var formatted = new FormattedText(layout.Text, CultureInfo.CurrentUICulture,
            System.Windows.FlowDirection.LeftToRight, LabelTypeface, RadialTypography.FontSize,
            new SolidColorBrush(Color.FromRgb(37, 47, 58)), pixelsPerDip)
        {
            TextAlignment = TextAlignment.Center,
            LineHeight = RadialTypography.LineHeight, MaxLineCount = layout.Text.Count(c => c == '\n') + 1
        };
        // The formatter has already chosen all line breaks. Do not let WPF
        // wrap a second time because advance width includes blank side bearings.
        formatted.MaxTextWidth = formatted.WidthIncludingTrailingWhitespace + 1;
        var ink = formatted.BuildGeometry(new Point()).Bounds;
        var top = ink.IsEmpty ? layout.Bounds.Top : content.Center.Y - ink.Top - ink.Height / 2;
        var left = ink.IsEmpty ? layout.Bounds.Left : content.Center.X - ink.Left - ink.Width / 2;
        return _labelCache[key] = new(layout, formatted, new Point(left, top));
    }

    public void PlayOpen()
    {
        BeginAnimation(OpenProgressProperty, null);
        OpenProgress = 1;
        if (UiMotion.Enabled)
            BeginAnimation(OpenProgressProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(420))
            { FillBehavior = FillBehavior.Stop });
    }

    private void UpdateEntryBreathing()
    {
        var active = UiMotion.Enabled && IsVisible && _session is not null && _layout is not null &&
            RadialInteractionPolicy.RequiresEntryArc(_layout, _visualHover) &&
            _session.GetFolder(_visualHover.RingIndex, _visualHover.SlotIndex) is { } folder && CanEnter(folder);
        if (_breathing == active) return;
        _breathing = active;
        BeginAnimation(EntryBreathProperty, null); EntryBreath = 0;
        if (active) BeginAnimation(EntryBreathProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(1100))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            });
    }

    private void DrawEntryArc(DrawingContext dc)
    {
        if (_layout is null || _session is null ||
            !RadialInteractionPolicy.RequiresEntryArc(_layout, _visualHover)) return;
        var folder = _session.GetFolder(_visualHover.RingIndex, _visualHover.SlotIndex);
        if (folder is null || !CanEnter(folder)) return;
        var arc = RadialInteractionPolicy.EntryArc(_layout, _visualHover, VisibleSlots(_visualHover.RingIndex));
        var amount = GetHoverAmount(_visualHover.RingIndex, _visualHover.SlotIndex);
        if (amount < .01) return;
        var geometry = RadialGeometry.Arc(_layout.LocalCenter, arc.Radius, arc.Start,
            arc.Start + (arc.End - arc.Start) * Math.Max(.02, amount));
        var pending = _entryIntent.IsPending;
        var halo = new Pen(new SolidColorBrush(Color.FromArgb((byte)(pending ? 58 : 18 + EntryBreath * 25), 46, 99, 171)), 12)
        { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        var line = new Pen(new SolidColorBrush(Color.FromRgb(32, 78, 145)), pending ? 4.8 : 3.8)
        { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        dc.DrawGeometry(null, halo, geometry);
        dc.PushOpacity(pending || !UiMotion.Enabled ? 1 : .58 + EntryBreath * .42);
        dc.DrawGeometry(null, line, geometry); dc.Pop();
    }

    protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_session is null || IsTransitioning) return;
        if (e.Key is Key.Enter or Key.Space)
        {
            if (!e.IsRepeat)
            {
                if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 &&
                    _visualHover.RingIndex >= 0 && _session.GetFolder(_visualHover.RingIndex, _visualHover.SlotIndex) is { } selected)
                    Confirmed?.Invoke(selected.RelativePath);
                else if (_visualHover.RingIndex >= 0) EnterFolder(_visualHover);
                else if (_session.CurrentDestination is { } target) Confirmed?.Invoke(target.RelativePath);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Home) { StartHoverTransition(RadialHit.Center); UpdateDetails(); e.Handled = true; }
        else if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            CancelIntent(true);
            var ring = _visualHover.RingIndex < 0 ? 0 : _visualHover.RingIndex;
            var slot = Math.Max(0, _visualHover.SlotIndex);
            if (e.Key is Key.Up or Key.Down)
                ring = Math.Clamp(ring + (e.Key == Key.Down ? 1 : -1), 0, 2);
            else slot = (slot + (e.Key is Key.Right or Key.Down ? 1 : 3)) % Math.Max(1, VisibleSlots(ring));
            if (_session.GetFolder(ring, slot) is not null) { StartHoverTransition(new RadialHit(ring, slot, false)); UpdateDetails(); }
            e.Handled = true;
        }
    }

    private static double EaseOut(double progress) => 1 - Math.Pow(1 - Math.Clamp(progress, 0, 1), 3);

    private void DrawRings(
        DrawingContext drawingContext,
        RadialLayoutPlan layout,
        RadialMenuSession session)
    {
        // Keep the target frame stable while its animation runs. Asynchronous
        // child loads are reconciled at the boundary, never halfway through it.
        var currentViews = IsTransitioning ? _observedViews : CaptureRingViews();
        var progress = Math.Clamp(RingTransitionProgress, 0, 1);
        for (var ringIndex = 0; ringIndex < layout.Rings.Count; ringIndex++)
        {
            var opening = UiMotion.Enabled ? EaseOut((OpenProgress * 420 - ringIndex * 40) / 320) : 1;
            drawingContext.PushOpacity(opening);
            drawingContext.PushTransform(new ScaleTransform(.88 + .12 * opening, .88 + .12 * opening,
                layout.LocalCenter.X, layout.LocalCenter.Y));
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
                    // The surface stays still while the two content layers
                    // hand over. Names never inherit rotation or scale.
                    DrawRingView(drawingContext, layout, ringIndex, current,
                        Math.Min(1, progress * 3), 1, 0, false, contentOpacity: 0);
                    DrawRingView(drawingContext, layout, ringIndex, previous,
                        1 - EaseOut(progress * 2.4), 1, 0, false, contentOnly: true,
                        contentRadialOffset: -12 * _ringTransitionDirection * progress);
                    DrawRingView(drawingContext, layout, ringIndex, current,
                        EaseOut((progress - .08) / .92), 1, 0, true, contentOnly: true,
                        contentRadialOffset: 18 * _ringTransitionDirection * (1 - EaseOut(progress)));
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
            drawingContext.Pop();
            drawingContext.Pop();
        }

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
        var slotSweep = ring.SweepAngle / RadialLayout.SlotsPerRing;
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
            // A closed ring wraps the entering cell underneath the departing
            // one. Hand it over during rotation instead of uncovering its name
            // abruptly when the outgoing layer is removed on the final frame.
            drawingContext.PushOpacity(ring.SweepAngle >= 359.999 ? 1 - EaseOut(progress) : 1);
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
            drawingContext.Pop();
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
        bool interactive,
        bool contentOnly = false,
        double contentOpacity = 1,
        double contentRadialOffset = 0)
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
                angleOffset, contentOnly, contentOpacity, contentRadialOffset);
        }

        drawingContext.Pop();
        drawingContext.Pop();
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
        double angleOffset,
        bool contentOnly = false,
        double contentOpacity = 1,
        double contentRadialOffset = 0)
    {
        var slotSweep = ring.SweepAngle / visibleCount;
        var angularGap = RadialLayout.CellGap(ring, visibleCount);
        var ringStart = RadialLayout.SlotStart(ring, visibleCount);
        var start = ringStart + slot * slotSweep + angularGap;
        var end = ringStart + (slot + 1) * slotSweep - angularGap;
        var inner = ring.InnerRadius;
        var outer = ring.OuterRadius;
        var midAngle = (start + end) / 2;
        hoverAmount = Math.Clamp(hoverAmount, 0, 1);
        var radians = midAngle * Math.PI / 180;

        var geometry = CellGeometry(layout, inner, outer, start, end);
        // Animate the cached cell, not its layout inputs. Re-measuring text
        // at every rotated angle changed wrapping and rebuilt glyph geometry.
        if (angleOffset != 0)
            drawingContext.PushTransform(new RotateTransform(angleOffset, layout.LocalCenter.X, layout.LocalCenter.Y));
        var baseFill = folder is null
            ? Color.FromArgb(36, 242, 247, 252)
            : isSelected
                ? Color.FromRgb(230, 239, 250)
                : Color.FromRgb(247, 250, 253);
        var fill = new SolidColorBrush(Blend(
            baseFill,
            Color.FromRgb(216, 233, 255),
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
        if (!contentOnly) drawingContext.DrawGeometry(fill, stroke, geometry);

        if (folder is not null && contentOpacity > .001)
        {
            drawingContext.PushOpacity(contentOpacity);
            drawingContext.PushClip(geometry);
            var center = Polar(layout.LocalCenter, (inner + outer) / 2, midAngle);
            var content = CellContent(geometry, center);
            drawingContext.PushTransform(new TranslateTransform(contentRadialOffset * Math.Cos(radians), contentRadialOffset * Math.Sin(radians)));
            var label = FormatLabel(folder.DisplayName, content);
            if (angleOffset != 0)
                drawingContext.PushTransform(new RotateTransform(-angleOffset, content.Center.X, content.Center.Y));
            if (label.Text is not null) drawingContext.DrawText(label.Text, label.Origin);
            if (angleOffset != 0) drawingContext.Pop();
            drawingContext.Pop();
            drawingContext.Pop();
            drawingContext.Pop();
        }
        if (angleOffset != 0) drawingContext.Pop();
    }

    private void DrawCenter(DrawingContext dc, RadialLayoutPlan layout, RadialMenuSession? session)
    {
        var center = new Point(layout.LocalCenter.X, layout.LocalCenter.Y);
        var radius = layout.CenterRadius;
        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(30, 24, 44, 69)), null,
            new Point(center.X, center.Y + 3), radius + 2, radius + 2);
        dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(249, 252, 255)),
            new Pen(new SolidColorBrush(Color.FromRgb(206, 223, 243)), 1.2), center, radius, radius);
        var visible = Rect.Intersect(new Rect(center.X - radius, center.Y - radius, radius * 2, radius * 2),
            new Rect(0, 0, layout.WindowBounds.Width, layout.WindowBounds.Height));
        if (visible.Width < radius * 1.8 || visible.Height < radius * 1.8)
        {
            DrawCenteredText(dc, layout.IsInvalidTarget ? "中键选文件" : session?.CurrentDestination is null ? "选择" : "移动",
                visible.Left + visible.Width / 2, visible.Top + visible.Height / 2 - 8,
                Math.Max(18, visible.Width - 10), 11, LabelTypeface, Color.FromRgb(37, 99, 184), 16, true);
            return;
        }
        if (layout.IsInvalidTarget)
        {
            DrawCenteredText(dc, "请在文件或\n文件夹上\n点击鼠标中键", center.X, center.Y - 25,
                radius * 1.6, 11, RegularTypeface, Color.FromRgb(58, 78, 101), 17, false);
            return;
        }
        DrawCenteredText(dc, _sourceItemCount > 1 ? $"{_sourceItemCount} 个项目" : "1 个文件",
            center.X, center.Y - 27, radius * 1.6, 10.5, RegularTypeface,
            Color.FromRgb(101, 118, 139), 16, false);
        DrawCenteredText(dc, session?.CurrentDestination is null ? "选择目录" : "确认移动",
            center.X, center.Y - 7, radius * 1.65, 12.5, LabelTypeface,
            Color.FromRgb(37, 99, 184), 19, false);
        DrawCenteredText(dc, session?.CurrentDestination?.DisplayName ?? "可先浏览",
            center.X, center.Y + 17, radius * 1.4, RadialTypography.FontSize, LabelTypeface,
            Color.FromRgb(71, 96, 124), 15, true);
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

    private static Geometry CreateRingClip(RadialLayoutPlan layout, RadialRing ring)
    {
        var innerRadius = Math.Max(1, ring.InnerRadius - 4);
        var outerRadius = ring.OuterRadius + 5;
        if (ring.SweepAngle >= 359.999)
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

        var startAngle = ring.StartAngle;
        var endAngle = ring.StartAngle + ring.SweepAngle;
        var geometry = new StreamGeometry { FillRule = FillRule.EvenOdd };
        using (var context = geometry.Open())
        {
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
                ring.SweepAngle > 180,
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
                ring.SweepAngle > 180,
                SweepDirection.Counterclockwise,
                true,
                true);
        }
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

    private bool RequestWheelRotation(int ring, int direction, int slot, RadialRingView[]? displayedViews = null)
    {
        if (_session is null || ring is < 0 or > 2) return false;
        direction = Math.Sign(direction);
        if (direction == 0) return false;

        if (IsTransitioning)
        {
            if (_queuedWheelSteps == 0 || _queuedWheelRingIndex == ring)
                _queuedWheelSteps = Math.Clamp(_queuedWheelSteps + direction, -3, 3);
            else
                _queuedWheelSteps = direction;
            _queuedWheelRingIndex = ring;
            _queuedWheelSlot = Math.Clamp(slot, 0, RadialLayout.SlotsPerRing - 1);
            return true;
        }

        var previousViews = displayedViews ?? CaptureRingViews();
        CancelIntent(true); _entryIntent.Lock();
        bool rotated;
        _applyingWheelRotation = true;
        try { rotated = _session.RotateAndPreview(ring, direction, slot); }
        finally { _applyingWheelRotation = false; }
        if (!rotated) return false;
        StartRingTransition(previousViews, direction, ring);
        return true;
    }

    private bool StartQueuedWheelRotation(RadialRingView[] displayedViews)
    {
        while (_session is not null && _queuedWheelSteps != 0)
        {
            var direction = Math.Sign(_queuedWheelSteps);
            var ring = _queuedWheelRingIndex;
            var slot = _queuedWheelSlot;
            _queuedWheelSteps -= direction;
            if (_queuedWheelSteps == 0) _queuedWheelRingIndex = -1;
            if (RequestWheelRotation(ring, direction, slot, displayedViews)) return true;
        }
        return false;
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

    private void StartRingTransition(
        RadialRingView[] previousViews,
        int direction,
        int wheelRingIndex = -1,
        bool contentChangesOnly = false)
    {
        var currentViews = CaptureRingViews();
        _observedViews = currentViews;
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

        DismissNameTip();
        _previousViews = previousViews;
        _entryTimer.Stop(); _entryIntent.Lock();
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
        // A newly attached WPF clock starts on the next timing tick. The base
        // value must already describe the first frame, not flash the final one.
        RingTransitionProgress = 0;
        if (!UiMotion.Enabled)
        {
            _previousViews = null; _wheelTransitionActive = false;
            RingTransitionProgress = 1; UpdateDetails(); return;
        }
        var duration = TimeSpan.FromMilliseconds(wheelRingIndex >= 0 ? UiMotion.Wheel : UiMotion.Content);
        var animation = new DoubleAnimationUsingKeyFrames
        {
            Duration = new Duration(duration),
            FillBehavior = FillBehavior.Stop
        };
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(
            0,
            KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(duration)));
        animation.Completed += (_, _) =>
        {
            if (generation != _ringTransitionGeneration) return;
            var completedViews = _observedViews;
            _previousViews = null;
            RingTransitionProgress = 1;
            _wheelTransitionRingIndex = -1;
            _ringTransitionContentOnly = false;
            _wheelTransitionActive = false;
            InvalidateVisual();
            UpdateDetails();
            UpdateEntryBreathing();
            if (StartQueuedWheelRotation(completedViews)) return;
            // Present any data received during the animation using its own
            // content transition, once the user's queued movement is complete.
            StartRingTransition(completedViews, 1, contentChangesOnly: true);
            if (IsTransitioning) return;
            if (_entryIntent.IsPending && _entryNode is not null) _entryTimer.Start();
            else if (_visualHover.HasValue) _nameTimer.Start();
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
        UpdateEntryBreathing();
        DismissNameTip();
        if (next.HasValue) _nameTimer.Start();
        var generation = ++_hoverTransitionGeneration;
        BeginAnimation(HoverTransitionProgressProperty, null);
        HoverTransitionProgress = 1;
        if (!UiMotion.Enabled) { _previousVisualHover = RadialHit.None; InvalidateVisual(); return; }
        var animation = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(UiMotion.Hover))
        {
            EasingFunction = UiMotion.EaseOut(),
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
        if (IsProtectedEntryHit(_visualHover, point)) return _visualHover;
        var first = VisibleSlots(0);
        var second = VisibleSlots(1);
        var third = VisibleSlots(2);
        var hit = RadialLayout.HitTest(
            _layout,
            new RadialPoint(point.X, point.Y),
            first,
            second,
            third);
        if (hit.RingIndex < 0) return hit;
        var ring = _layout.Rings[hit.RingIndex];
        var gap = RadialLayout.CellGap(ring, VisibleSlots(hit.RingIndex));
        var sweep = RadialLayout.SlotSweep(ring, VisibleSlots(hit.RingIndex));
        var start = RadialLayout.SlotStart(ring, VisibleSlots(hit.RingIndex)) + hit.SlotIndex * sweep + gap;
        var shape = CellGeometry(_layout, ring.InnerRadius, ring.OuterRadius, start, start + sweep - 2 * gap);
        return shape.FillContains(point) ? hit : RadialHit.None;
    }

    private void BeginFolderPrefetch(FolderMenuNode folder)
    {
        if (_session is null) return;
        if (_session.Prefetch(folder)) InvalidateVisual();
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
        if (_layout is not null && RadialInteractionPolicy.RequiresEntryArc(_layout, hit)) return;
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
        if (IsTransitioning || _applyingWheelRotation) return;
        UpdateEntryBreathing();
        var previous = _observedViews;
        if (previous is { Length: > 0 })
            StartRingTransition(
                previous,
                1,
                contentChangesOnly: true);
        else
            InvalidateVisual();
        UpdateDetails();
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
