using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using GongSiOrganizer.Core;
using GongSiOrganizer.RadialPrototype.Services;
using Brushes = System.Windows.Media.Brushes;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace GongSiOrganizer.RadialPrototype.Radial;

internal sealed class RadialOverlayWindow : Window
{
    private readonly RadialMenuControl _control;
    private OverlayPlacement? _placement;
    private IReadOnlyList<string> _sourceFiles = [];
    private bool _isDemo;
    private bool _folderNameDialogOpen;

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
        Content = _control;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    public event Action<IReadOnlyList<string>, string>? MoveRequested;
    public event Action<string>? DemoConfirmed;
    public event Action<string, bool>? CreateFolderRequested;

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
        var placement = NativeDisplay.CreatePlacement(point, false);
        var session = new RadialMenuSession(snapshot.Folders);
        if (primeForDemo) session.PrimeForDemo();
        ShowCore(placement, session, _sourceFiles.Count);
    }

    public void ShowInvalid(NativeScreenPoint point)
    {
        _sourceFiles = [];
        _isDemo = false;
        ShowCore(NativeDisplay.CreatePlacement(point, true), null, 0);
    }

    public void HideOverlay()
    {
        if (IsVisible) Hide();
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

    public void RefreshMenu(FolderCatalogResult snapshot, string selectedRelativePath)
    {
        if (!IsVisible || _placement is null) return;
        var session = new RadialMenuSession(snapshot.Folders);
        _ = session.SelectPath(selectedRelativePath);
        _control.Configure(_placement.Layout, session, _sourceFiles.Count);
        _control.Focus();
    }

    public void SetInteractionEnabled(bool enabled) =>
        _control.IsHitTestVisible = enabled;

    public bool ShouldDismissForGlobalClick(NativeScreenPoint point)
    {
        if (!IsVisible || _folderNameDialogOpen || _placement is null) return false;
        var localPoint = new System.Windows.Point(
            (point.X - _placement.ScreenLeftPixels) / _placement.DpiScale,
            (point.Y - _placement.ScreenTopPixels) / _placement.DpiScale);
        return !_control.HasMenuHit(localPoint);
    }

    public bool RotateDemo(int direction) => _control.RotateDemo(direction);

    public bool HoverDemo(int ringIndex) => _control.HoverDemo(ringIndex);

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
        bitmap.Render(_control);

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
        _placement = placement;
        Width = placement.Layout.WindowBounds.Width;
        Height = placement.Layout.WindowBounds.Height;
        _control.Width = Width;
        _control.Height = Height;
        _control.Configure(placement.Layout, session, sourceItemCount);

        if (!IsVisible) Show();
        NativeDisplay.PositionWindow(this, placement);
        Activate();
        _control.Focus();
        PlayOpenAnimation(placement.Layout.LocalCenter);
    }

    private void PlayOpenAnimation(RadialPoint localCenter)
    {
        var scale = new ScaleTransform(0.92, 0.92, localCenter.X, localCenter.Y);
        _control.RenderTransform = scale;
        _control.Opacity = 0;
        var scaleEasing = new SineEase { EasingMode = EasingMode.EaseOut };
        var opacityEasing = new SineEase { EasingMode = EasingMode.EaseOut };
        scale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.92, 1, TimeSpan.FromMilliseconds(175))
            {
                EasingFunction = scaleEasing
            });
        scale.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.92, 1, TimeSpan.FromMilliseconds(175))
            {
                EasingFunction = scaleEasing
            });
        _control.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(145))
            {
                EasingFunction = opacityEasing
            });
    }

    private void OnConfirmed(string relativePath)
    {
        if (_sourceFiles.Count == 0) return;
        var sourceFiles = _sourceFiles;
        var isDemo = _isDemo;
        HideOverlay();
        if (isDemo)
            DemoConfirmed?.Invoke(relativePath);
        else
            MoveRequested?.Invoke(sourceFiles, relativePath);
    }

    private void OnCreateFolderRequested(string relativePath) =>
        CreateFolderRequested?.Invoke(relativePath, _isDemo);

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
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
