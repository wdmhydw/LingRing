using System.IO;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace GongSiOrganizer.RadialPrototype.Services;

internal sealed class TrayIconService : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Drawing.Icon _applicationIcon;
    private readonly TrayMenuWindow _menuWindow;
    private readonly CompactToastService _toast = new();

    public TrayIconService(
        Action showDemo,
        Action refresh,
        Action changeRoot,
        Action resetUsage,
        Action showOnboarding,
        Action<bool> setPaused,
        Action exit,
        string initialRoot)
    {
        _applicationIcon = LoadApplicationIcon();
        _menuWindow = new TrayMenuWindow(
            initialRoot,
            showDemo,
            refresh,
            changeRoot,
            resetUsage,
            showOnboarding,
            setPaused,
            exit);
        _icon = new Forms.NotifyIcon
        {
            Icon = _applicationIcon,
            Text = "文件快速归档",
            Visible = true
        };
        _icon.MouseUp += OnIconMouseUp;
        _icon.DoubleClick += (_, _) => InvokeOnUi(showDemo);
    }

    public void SetArchiveRoot(string rootPath) =>
        InvokeOnUi(() => _menuWindow.SetArchiveRoot(rootPath));

    public void ShowMoveSuccess(string title, string message)
    {
        if (!_menuWindow.SuccessNoticesEnabled) return;
        ShowToast(title, message, CompactToastKind.Success, 1700);
    }

    public void ShowStatus(string title, string message, int timeout = 2200)
    {
        ShowToast(title, message, CompactToastKind.Info, Math.Clamp(timeout, 900, 5000));
    }

    public void ShowWarning(string title, string message, int timeout = 4200)
    {
        ShowToast(title, message, CompactToastKind.Warning, Math.Clamp(timeout, 1800, 8000));
    }

    public void SetBusyStatus(string? status)
    {
        var text = string.IsNullOrWhiteSpace(status)
            ? "文件快速归档"
            : status;
        _icon.Text = text.Length <= 63 ? text : text[..63];
    }

    public void ShowMenuAt(NativeScreenPoint point) =>
        InvokeOnUi(() => _menuWindow.ShowAt(point));

    public void SaveMenuSnapshot(string path) => _menuWindow.SaveSnapshot(path);

    public void Dispose()
    {
        _icon.MouseUp -= OnIconMouseUp;
        _icon.Visible = false;
        _icon.Dispose();
        _menuWindow.Close();
        _applicationIcon.Dispose();
        _toast.Dispose();
    }

    private void OnIconMouseUp(object? sender, Forms.MouseEventArgs e)
    {
        if (e.Button != Forms.MouseButtons.Right) return;
        NativeScreenPoint point;
        try
        {
            point = NativeDisplay.GetCursor();
        }
        catch
        {
            point = new NativeScreenPoint(e.X, e.Y);
        }
        InvokeOnUi(() => _menuWindow.ShowAt(point));
    }

    private void ShowToast(
        string title,
        string message,
        CompactToastKind kind,
        int durationMilliseconds)
    {
        try
        {
            _toast.Show(
                title,
                message,
                kind,
                TimeSpan.FromMilliseconds(durationMilliseconds));
        }
        catch
        {
            // A status surface is optional and must never interrupt a move.
        }
    }

    private static void InvokeOnUi(Action action)
    {
        var dispatcher = System.Windows.Application.Current.Dispatcher;
        if (dispatcher.CheckAccess())
            action();
        else
            _ = dispatcher.InvokeAsync(action);
    }

    private static Drawing.Icon LoadApplicationIcon()
    {
        try
        {
            var processPath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(processPath))
            {
                var extracted = Drawing.Icon.ExtractAssociatedIcon(processPath);
                if (extracted is not null) return extracted;
            }
        }
        catch
        {
            // Fall through to a private clone of the system icon.
        }

        return (Drawing.Icon)Drawing.SystemIcons.Application.Clone();
    }
}
