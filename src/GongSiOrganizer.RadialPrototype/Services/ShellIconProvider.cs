using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GongSiOrganizer.RadialPrototype.Services;

internal static class ShellIconProvider
{
    private const uint ShgfiIcon = 0x000000100;
    private const uint ShgfiSmallIcon = 0x000000001;
    private const uint ShgfiUseFileAttributes = 0x000000010;
    private const uint FileAttributeDirectory = 0x00000010;

    public static ImageSource? LoadFolderIcon()
    {
        var info = new ShellFileInfo();
        var result = SHGetFileInfo(
            "folder",
            FileAttributeDirectory,
            ref info,
            (uint)Marshal.SizeOf<ShellFileInfo>(),
            ShgfiIcon | ShgfiSmallIcon | ShgfiUseFileAttributes);
        if (result == IntPtr.Zero || info.Icon == IntPtr.Zero)
            return null;

        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(
                info.Icon,
                Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(20, 20));
            source.Freeze();
            return source;
        }
        finally
        {
            DestroyIcon(info.Icon);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileInfo
    {
        public IntPtr Icon;
        public int IconIndex;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(
        string path,
        uint fileAttributes,
        ref ShellFileInfo fileInfo,
        uint fileInfoSize,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);
}
