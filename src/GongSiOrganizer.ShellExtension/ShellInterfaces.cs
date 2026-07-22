using System.Runtime.InteropServices;

namespace GongSiOrganizer.ShellExtension;

[ComVisible(true)]
[Guid("000214E8-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IShellExtInit
{
    [PreserveSig]
    int Initialize(IntPtr folderPidl, IntPtr dataObject, IntPtr programIdKey);
}

[ComVisible(true)]
[Guid("000214E4-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IContextMenu
{
    [PreserveSig]
    int QueryContextMenu(
        IntPtr menu,
        uint indexMenu,
        uint commandIdFirst,
        uint commandIdLast,
        uint flags);

    [PreserveSig]
    int InvokeCommand(IntPtr commandInfo);

    [PreserveSig]
    int GetCommandString(
        UIntPtr commandOffset,
        uint type,
        IntPtr reserved,
        IntPtr name,
        uint maximumCharacters);
}

[ComVisible(true)]
[Guid("000214F4-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IContextMenu2
{
    [PreserveSig]
    int QueryContextMenu(
        IntPtr menu,
        uint indexMenu,
        uint commandIdFirst,
        uint commandIdLast,
        uint flags);

    [PreserveSig]
    int InvokeCommand(IntPtr commandInfo);

    [PreserveSig]
    int GetCommandString(
        UIntPtr commandOffset,
        uint type,
        IntPtr reserved,
        IntPtr name,
        uint maximumCharacters);

    [PreserveSig]
    int HandleMenuMsg(uint message, IntPtr wParam, IntPtr lParam);
}

[ComVisible(true)]
[Guid("BCFCE0A0-EC17-11D0-8D10-00A0C90F2719")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IContextMenu3
{
    [PreserveSig]
    int QueryContextMenu(
        IntPtr menu,
        uint indexMenu,
        uint commandIdFirst,
        uint commandIdLast,
        uint flags);

    [PreserveSig]
    int InvokeCommand(IntPtr commandInfo);

    [PreserveSig]
    int GetCommandString(
        UIntPtr commandOffset,
        uint type,
        IntPtr reserved,
        IntPtr name,
        uint maximumCharacters);

    [PreserveSig]
    int HandleMenuMsg(uint message, IntPtr wParam, IntPtr lParam);

    [PreserveSig]
    int HandleMenuMsg2(uint message, IntPtr wParam, IntPtr lParam, out IntPtr result);
}

[StructLayout(LayoutKind.Sequential)]
internal struct CommandInfo
{
    public uint Size;
    public uint Mask;
    public IntPtr Window;
    public IntPtr Verb;
    public IntPtr Parameters;
    public IntPtr Directory;
    public int Show;
    public uint HotKey;
    public IntPtr Icon;
}
