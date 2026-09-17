using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using GongSiOrganizer.RadialPrototype.Radial;

namespace GongSiOrganizer.RadialPrototype.Services;

internal readonly record struct NativeScreenPoint(int X, int Y);

internal readonly record struct NativeHitContext(
    NativeScreenPoint Point,
    IntPtr WindowHandle,
    IntPtr RootWindowHandle);

internal sealed record OverlayPlacement(
    RadialLayoutPlan Layout,
    int ScreenLeftPixels,
    int ScreenTopPixels,
    int WidthPixels,
    int HeightPixels,
    double DpiScale);

internal static class NativeDisplay
{
    private const uint MonitorDefaultToNearest = 2;
    private const uint GaRoot = 2;
    private const int SwpNoActivate = 0x0010;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaBorderColor = 34;
    private const int DwmWindowCornerPreferenceRound = 2;
    private const int DwmColorNone = -2;
    private static readonly IntPtr HwndTopmost = new(-1);

    public static OverlayPlacement CreatePlacement(NativeScreenPoint click, bool invalidTarget)
    {
        var nativePoint = new NativePoint(click.X, click.Y);
        var monitor = MonitorFromPoint(nativePoint, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info))
            throw new InvalidOperationException("无法读取鼠标所在显示器的工作区域。");

        var dpiScale = GetScale(monitor);
        var workWidthDip = (info.Work.Right - info.Work.Left) / dpiScale;
        var workHeightDip = (info.Work.Bottom - info.Work.Top) / dpiScale;
        var clickDip = new RadialPoint(
            (click.X - info.Work.Left) / dpiScale,
            (click.Y - info.Work.Top) / dpiScale);
        var layout = RadialLayout.Create(
            clickDip,
            new RadialRect(0, 0, workWidthDip, workHeightDip),
            invalidTarget);

        var leftPx = info.Work.Left + (int)Math.Round(layout.WindowBounds.X * dpiScale);
        var topPx = info.Work.Top + (int)Math.Round(layout.WindowBounds.Y * dpiScale);
        var widthPx = Math.Max(1, (int)Math.Ceiling(layout.WindowBounds.Width * dpiScale));
        var heightPx = Math.Max(1, (int)Math.Ceiling(layout.WindowBounds.Height * dpiScale));
        return new OverlayPlacement(layout, leftPx, topPx, widthPx, heightPx, dpiScale);
    }

    public static NativeScreenPoint GetCursor()
    {
        if (!GetCursorPos(out var point))
            throw new InvalidOperationException("无法读取鼠标坐标。");
        return new NativeScreenPoint(point.X, point.Y);
    }

    public static void ConfigurePopupChrome(Window window)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return;
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;

        try
        {
            var cornerPreference = DwmWindowCornerPreferenceRound;
            _ = DwmSetWindowAttribute(
                handle,
                DwmwaWindowCornerPreference,
                ref cornerPreference,
                sizeof(int));
            var borderColor = DwmColorNone;
            _ = DwmSetWindowAttribute(
                handle,
                DwmwaBorderColor,
                ref borderColor,
                sizeof(int));
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }
    }

    public static NativeHitContext CaptureHitContext() => CaptureHitContext(GetCursor());

    public static NativeHitContext CaptureHitContext(NativeScreenPoint point)
    {
        var nativePoint = new NativePoint(point.X, point.Y);
        var window = WindowFromPoint(nativePoint);
        var root = window == IntPtr.Zero ? IntPtr.Zero : GetAncestor(window, GaRoot);
        return new NativeHitContext(point, window, root);
    }

    public static NativeScreenPoint GetPrimaryWorkAreaCenter(bool nearCorner = false)
        => GetPrimaryWorkAreaPoint(nearCorner ? "top-left" : "center");

    public static NativeScreenPoint GetPrimaryWorkAreaPoint(string position)
    {
        var monitor = MonitorFromPoint(new NativePoint(0, 0), MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) return new NativeScreenPoint(600, 420);
        var cx = (info.Work.Left + info.Work.Right) / 2;
        var cy = (info.Work.Top + info.Work.Bottom) / 2;
        var scale = GetScale(monitor);
        return position switch
        {
            "left" => new(info.Work.Left + 75, cy),
            "right" => new(info.Work.Right - 75, cy),
            "top" => new(cx, info.Work.Top + 75),
            "bottom" => new(cx, info.Work.Bottom - 75),
            "top-left" => new(info.Work.Left + 150, info.Work.Top + 150),
            "top-right" => new(info.Work.Right - 150, info.Work.Top + 150),
            "bottom-left" => new(info.Work.Left + 150, info.Work.Bottom - 150),
            "bottom-right" => new(info.Work.Right - 150, info.Work.Bottom - 150),
            "bottom-left-tight" => new(info.Work.Left + (int)Math.Round(50 * scale),
                info.Work.Bottom - (int)Math.Round(80 * scale)),
            "left-inset" => new(info.Work.Left + (int)Math.Round(200 * scale), cy),
            _ => new(cx, cy)
        };
    }

    public static void PositionWindow(Window window, OverlayPlacement placement)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        SetWindowPos(
            handle,
            HwndTopmost,
            placement.ScreenLeftPixels,
            placement.ScreenTopPixels,
            placement.WidthPixels,
            placement.HeightPixels,
            SwpNoActivate);
    }

    public static void PositionPopupWindow(
        Window window,
        NativeScreenPoint anchor,
        double widthDip,
        double heightDip)
    {
        var nativePoint = new NativePoint(anchor.X, anchor.Y);
        var monitor = MonitorFromPoint(nativePoint, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return;

        var scale = GetScale(monitor);
        var widthPixels = Math.Max(1, (int)Math.Ceiling(widthDip * scale));
        var heightPixels = Math.Max(1, (int)Math.Ceiling(heightDip * scale));
        var gap = Math.Max(8, (int)Math.Round(10 * scale));
        var minimumX = info.Work.Left + gap;
        var maximumX = Math.Max(minimumX, info.Work.Right - widthPixels - gap);
        var minimumY = info.Work.Top + gap;
        var maximumY = Math.Max(minimumY, info.Work.Bottom - heightPixels - gap);

        var left = anchor.X < info.Work.Left
            ? minimumX
            : anchor.X > info.Work.Right
                ? maximumX
                : Math.Clamp(anchor.X - widthPixels + gap, minimumX, maximumX);
        var top = anchor.Y < info.Work.Top
            ? minimumY
            : anchor.Y > info.Work.Bottom
                ? maximumY
                : Math.Clamp(anchor.Y - heightPixels - gap, minimumY, maximumY);

        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        SetWindowPos(
            handle,
            HwndTopmost,
            left,
            top,
            widthPixels,
            heightPixels,
            0);
    }

    private static double GetScale(IntPtr monitor)
    {
        try
        {
            var result = GetDpiForMonitor(monitor, 0, out var dpiX, out _);
            if (result == 0 && dpiX > 0) return dpiX / 96d;
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }

        return 1d;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public NativePoint(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(NativePoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr window, uint flags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr window,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        int flags);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr window,
        int attribute,
        ref int attributeValue,
        int attributeSize);
}
