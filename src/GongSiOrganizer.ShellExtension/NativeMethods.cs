using System.Runtime.InteropServices;
using System.Text;

namespace GongSiOrganizer.ShellExtension;

internal static class NativeMethods
{
    internal const uint MenuByPosition = 0x00000400;
    internal const uint MenuString = 0x00000000;
    internal const uint MenuPopup = 0x00000010;
    internal const uint MenuSeparator = 0x00000800;
    internal const uint MenuGrayed = 0x00000001;
    internal const uint InitMenuPopup = 0x0117;
    internal const uint ContextMenuDefaultOnly = 0x00000001;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AppendMenuW(
        IntPtr menu,
        uint flags,
        UIntPtr newItem,
        [MarshalAs(UnmanagedType.LPWStr)] string? text);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool InsertMenuW(
        IntPtr menu,
        uint position,
        uint flags,
        UIntPtr newItem,
        [MarshalAs(UnmanagedType.LPWStr)] string? text);

    [DllImport("user32.dll")]
    internal static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    internal static extern int GetMenuItemCount(IntPtr menu);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RemoveMenu(IntPtr menu, uint position, uint flags);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    internal static extern uint DragQueryFileW(
        IntPtr dropHandle,
        uint fileIndex,
        StringBuilder? fileName,
        uint characterCount);

    [DllImport("ole32.dll")]
    internal static extern void ReleaseStgMedium(ref System.Runtime.InteropServices.ComTypes.STGMEDIUM medium);
}
