using System.Runtime.InteropServices;

namespace GongSiOrganizer.App;

internal static class NativeDialog
{
    private const uint Ok = 0x00000000;
    private const uint IconInformation = 0x00000040;
    private const uint IconError = 0x00000010;
    private const uint SetForeground = 0x00010000;

    public static void Information(string message) =>
        MessageBoxW(IntPtr.Zero, message, "公司文件一键移动", Ok | IconInformation | SetForeground);

    public static void Error(string message) =>
        MessageBoxW(IntPtr.Zero, message, "公司文件一键移动", Ok | IconError | SetForeground);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBoxW(
        IntPtr windowHandle,
        string text,
        string caption,
        uint type);
}
