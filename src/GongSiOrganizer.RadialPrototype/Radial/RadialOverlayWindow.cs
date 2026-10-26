using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using GongSiOrganizer.Core;
using GongSiOrganizer.RadialPrototype.Presentation;
using GongSiOrganizer.RadialPrototype.Services;
using Brushes = System.Windows.Media.Brushes;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace GongSiOrganizer.RadialPrototype.Radial;

internal sealed class RadialOverlayWindow : Window
{
    private readonly RadialMenuControl _control;
    private readonly Canvas _surface;
    private readonly RadialNameTip _nameTip;
    private string _rootPath = "";
    private int _visibilityGeneration;
    private bool _closing;
    private OverlayPlacement? _placement;
    private IReadOnlyList<string> _sourceFiles = [];
    private bool _isDemo;
    private bool _folderNameDialogOpen;
    private MoveFeedbackWindow? _moveFeedback;

    public RadialOverlayWindow()
    {
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;

        _control = new RadialMenuControl();
        _control.Confirmed += OnConfirmed;
        _control.CreateFolderRequested += OnCreateFolderRequested;
        _control.DismissRequested += HideOverlay;
        _nameTip = new RadialNameTip(_control);
        _control.NameTipRequested += (folder, point) => _nameTip.ShowName(folder, _rootPath, _sourceFiles, point);
        _control.NameTipDismissRequested += () => _nameTip.IsOpen = false;
        _surface = new Canvas();
        _surface.Children.Add(_control);
        Content = _surface;
        Closed += (_, _) => { _control.ReleaseSession(); _moveFeedback?.Dismiss(); };
        PreviewKeyDown += OnPreviewKeyDown;
    }

    public event Action<IReadOnlyList<string>, string>? MoveRequested;
    public event Action<string>? DemoConfirmed;
    public event Action<string, bool>? CreateFolderRequested;
    public event Action? GuideRequested;

    public void ShowMenu(
        NativeScreenPoint point,
        IReadOnlyList<string> sourceFiles,
        FolderCatalogResult snapshot,
        bool primeForDemo = false)
    {
        _sourceFiles = sourceFiles
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _isDemo = primeForDemo;
        _rootPath = snapshot.RootPath;
        var placement = NativeDisplay.CreatePlacement(point, false);
        var session = new RadialMenuSession(snapshot.Folders);
        if (primeForDemo) session.PrimeForDemo();
        ShowCore(placement, session, _sourceFiles.Count);
    }

    public async Task ShowMenuAsync(
        NativeScreenPoint point,
        IReadOnlyList<string> sourceFiles,
        FolderCatalogResult snapshot,
        string? initialRelativePath,
        CancellationToken cancellationToken)
    {
        _sourceFiles = sourceFiles
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _isDemo = false;
        _rootPath = snapshot.RootPath;
        var placement = NativeDisplay.CreatePlacement(point, false);
        var session = new RadialMenuSession(snapshot.Folders);

        if (!string.IsNullOrWhiteSpace(initialRelativePath))
        {
            var candidate = session;
            var selection = candidate.SelectStartPathAsync(initialRelativePath);
            try
            {
                var selected = await selection
                    .WaitAsync(TimeSpan.FromMilliseconds(140), cancellationToken);
                if (!selected) session = new RadialMenuSession(snapshot.Folders);
            }
            catch (TimeoutException)
            {
                // Keep the wheel instant on a sleeping HDD/removable disk.
                // The abandoned lookup still warms the shared directory cache.
                session = new RadialMenuSession(snapshot.Folders);
            }
            catch (OperationCanceledException) { candidate.Dispose(); throw; }
            if (!ReferenceEquals(candidate, session)) candidate.Dispose();
        }

        if (cancellationToken.IsCancellationRequested) { session.Dispose(); cancellationToken.ThrowIfCancellationRequested(); }
        ShowCore(placement, session, _sourceFiles.Count);
    }

    public async Task ShowDemoAsync(NativeScreenPoint point, FolderCatalogResult snapshot, string? startPath)
    {
        var session = new RadialMenuSession(snapshot.Folders);
        if (!string.IsNullOrWhiteSpace(startPath)) await session.SelectStartPathAsync(startPath);
        for (var ring = 0; ring < 2; ring++)
        {
            var folder = session.GetFolder(ring, 0);
            if (folder is null) break;
            if (folder.Children is AsyncFolderMenuNodeList children) await children.EnsureLoadedAsync();
            session.Hover(ring, 0);
        }
        _sourceFiles = ["2026年度推广方案_客户终审确认版_09月17日.pptx"];
        _isDemo = true;
        _rootPath = snapshot.RootPath;
        ShowCore(NativeDisplay.CreatePlacement(point, false), session, 1);
    }

    public void ShowInvalid(NativeScreenPoint point)
    {
        _sourceFiles = [];
        _isDemo = false;
        ShowCore(NativeDisplay.CreatePlacement(point, true), null, 0);
    }

    public void HideOverlay()
    {
        if (!IsVisible || _closing) return;
        _closing = true; _control.CancelIntent(true); _surface.IsHitTestVisible = false;
        var generation = ++_visibilityGeneration;
        if (!UiMotion.Enabled) { HideImmediately(); return; }
        var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(UiMotion.Exit))
        { FillBehavior = FillBehavior.Stop };
        fade.Completed += (_, _) => { if (generation == _visibilityGeneration) HideImmediately(); };
        BeginAnimation(OpacityProperty, fade);
    }

    private void HideImmediately()
    {
        ++_visibilityGeneration; _closing = false;
        BeginAnimation(OpacityProperty, null); Opacity = 1;
        if (IsVisible) Hide();
        _control.ReleaseSession(); _surface.IsHitTestVisible = true;
    }

    public bool PromptForFolderName(
        string parentRelativePath,
        Func<string, string?> tryCreate)
    {
        if (!IsVisible) return false;
        _folderNameDialogOpen = true;
        try
        {
            return FolderNameDialog.Show(this, parentRelativePath, tryCreate);
        }
        finally
        {
            _folderNameDialogOpen = false;
            if (IsVisible)
            {
                Activate();
                _control.Focus();
            }
        }
    }

    public async Task RefreshMenuAsync(
        FolderCatalogResult snapshot,
        string selectedRelativePath,
        string? initialRelativePath = null)
    {
        if (!IsVisible || _closing || _placement is null) return;
        var session = new RadialMenuSession(snapshot.Folders);
        _ = await session.SelectPathAsync(
            selectedRelativePath,
            initialRelativePath);
        if (!IsVisible || _closing || _placement is null) return;
        _rootPath = snapshot.RootPath;
        _control.Configure(_placement.Layout, session, _sourceFiles.Count);
        _control.Focus();
    }

    public void SetInteractionEnabled(bool enabled) =>
        _control.IsHitTestVisible = enabled;

    public bool ShouldDismissForGlobalClick(NativeScreenPoint point)
    {
        if (!IsVisible || _closing || _folderNameDialogOpen || _placement is null) return false;
        var localPoint = new System.Windows.Point(
            (point.X - _placement.ScreenLeftPixels) / _placement.DpiScale,
            (point.Y - _placement.ScreenTopPixels) / _placement.DpiScale);
        return !_control.HasMenuHit(localPoint);
    }

    public bool RotateDemo(int direction) => _control.RotateDemo(direction);

    public bool HoverDemo(int ringIndex) => _control.HoverDemo(ringIndex);

    public MoveFeedbackWindow? StartMoveFeedback(bool demo = false)
    {
        if (!IsVisible || _placement is null) return null;
        _moveFeedback?.Dismiss();
        // Freeze the last menu picture before releasing the directory session and its timers.
        try
        {
            _control.UpdateLayout();
            var snapshot = UiMotion.Enabled ? VisualTreeHelper.GetDrawing(_control)?.CloneCurrentValue() : null;
            snapshot?.Freeze();
            _moveFeedback = new MoveFeedbackWindow(_placement, snapshot, demo);
            return _moveFeedback;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or ArgumentException)
        {
            // Feedback is optional; a display failure must not prevent the requested file move.
            return null;
        }
        finally { HideImmediately(); }
    }

    public void SaveSnapshot(string path)
    {
        var dpi = VisualTreeHelper.GetDpi(_control);
        var width = Math.Max(1, (int)Math.Ceiling(_control.ActualWidth * dpi.DpiScaleX));
        var height = Math.Max(1, (int)Math.Ceiling(_control.ActualHeight * dpi.DpiScaleY));
        var bitmap = new RenderTargetBitmap(
            width,
            height,
            dpi.PixelsPerInchX,
            dpi.PixelsPerInchY,
            PixelFormats.Pbgra32);
        bitmap.Render(_surface);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private void ShowCore(
        OverlayPlacement placement,
        RadialMenuSession? session,
        int sourceItemCount)
    {
        _moveFeedback?.Dismiss();
        _moveFeedback = null;
        _placement = placement;
        ++_visibilityGeneration; _closing = false;
        BeginAnimation(OpacityProperty, null); Opacity = 1; _surface.IsHitTestVisible = true;
        Width = placement.Layout.WindowBounds.Width;
        Height = placement.Layout.WindowBounds.Height;
        _control.Width = Width;
        _control.Height = Height;
        _surface.Width = Width; _surface.Height = Height;
        _control.Configure(placement.Layout, session, sourceItemCount);

        if (!IsVisible) Show();
        NativeDisplay.PositionWindow(this, placement);
        Activate();
        _control.Focus();
        PlayOpenAnimation(placement.Layout.LocalCenter);
    }

    private void PlayOpenAnimation(RadialPoint localCenter)
    {
        _control.RenderTransform = Transform.Identity;
        _control.Opacity = 1;
        _control.PlayOpen();
    }

    private void OnConfirmed(string relativePath)
    {
        if (_sourceFiles.Count == 0 || _closing) return;
        var sourceFiles = _sourceFiles;
        var isDemo = _isDemo;
        _surface.IsHitTestVisible = false;
        try
        {
            // The handler captures the menu before its first await, then starts the operation immediately.
            if (isDemo) DemoConfirmed?.Invoke(relativePath);
            else MoveRequested?.Invoke(sourceFiles, relativePath);
        }
        finally { if (IsVisible) HideImmediately(); }
    }

    private void OnCreateFolderRequested(string relativePath) =>
        CreateFolderRequested?.Invoke(relativePath, _isDemo);

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F1)
        {
            _control.CancelIntent(true);
            GuideRequested?.Invoke();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Escape)
        {
            HideOverlay();
            e.Handled = true;
            return;
        }

        if (e.Key is not (Key.Back or Key.BrowserBack)) return;
        if (!_control.NavigateBack()) HideOverlay();
        e.Handled = true;
    }
}
