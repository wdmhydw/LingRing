using System.Runtime.InteropServices;
using System.Text;

var invokeRoot = args.Length == 2 &&
                 args[0].Equals("--invoke-root", StringComparison.OrdinalIgnoreCase);
var inspectedPath = invokeRoot ? args[1] : args.FirstOrDefault();
if (string.IsNullOrWhiteSpace(inspectedPath) || !File.Exists(inspectedPath))
{
    Console.Error.WriteLine("Usage: ShellMenuProbe [--invoke-root] <existing-file>");
    return 2;
}

Console.Write(ShellMenuInspector.Inspect(inspectedPath, invokeRoot));
return 0;

internal static class ShellMenuInspector
{
    private const uint ByPosition = 0x400;
    private const uint InitMenuPopup = 0x0117;

    public static string Inspect(string path, bool invokeRoot)
    {
        IntPtr absolute = IntPtr.Zero;
        IntPtr menu = IntPtr.Zero;
        IntPtr contextPointer = IntPtr.Zero;
        IShellFolder? parent = null;
        IContextMenu? context = null;

        try
        {
            Marshal.ThrowExceptionForHR(SHParseDisplayName(path, IntPtr.Zero, out absolute, 0, out _));
            var folderId = typeof(IShellFolder).GUID;
            Marshal.ThrowExceptionForHR(SHBindToParent(absolute, ref folderId, out parent, out var child));
            var contextId = typeof(IContextMenu).GUID;
            Marshal.ThrowExceptionForHR(parent.GetUIObjectOf(
                IntPtr.Zero,
                1,
                [child],
                ref contextId,
                IntPtr.Zero,
                out contextPointer));

            context = (IContextMenu)Marshal.GetTypedObjectForIUnknown(contextPointer, typeof(IContextMenu));
            var context3 = (IContextMenu3)context;
            menu = CreatePopupMenu();
            var queryWatch = System.Diagnostics.Stopwatch.StartNew();
            var queryResult = context.QueryContextMenu(menu, 0, 1, 0x7fff, 0);
            queryWatch.Stop();
            Marshal.ThrowExceptionForHR(queryResult);

            var report = new StringBuilder();
            report.AppendLine($"QueryElapsedMs={queryWatch.ElapsedMilliseconds}");
            report.AppendLine($"QueryCommandCount={queryResult & 0xffff}");
            var rootPosition = Find(menu, "移动到公司目录");
            report.AppendLine($"RootPosition={rootPosition}");
            if (rootPosition < 0) return report.ToString();

            var rootSubmenu = GetSubMenu(menu, rootPosition);
            report.AppendLine($"RootHasSubmenu={rootSubmenu != IntPtr.Zero}");
            if (rootSubmenu == IntPtr.Zero) return report.ToString();

            context3.HandleMenuMsg2(InitMenuPopup, rootSubmenu, rootPosition, out _);
            AppendMenu(report, context3, rootSubmenu, 0, 4);
            AppendDirectExtensionProbe(report, parent, child, invokeRoot);
            return report.ToString();
        }
        finally
        {
            if (menu != IntPtr.Zero) DestroyMenu(menu);
            if (context is not null && Marshal.IsComObject(context)) Marshal.FinalReleaseComObject(context);
            if (contextPointer != IntPtr.Zero) Marshal.Release(contextPointer);
            if (parent is not null && Marshal.IsComObject(parent)) Marshal.FinalReleaseComObject(parent);
            if (absolute != IntPtr.Zero) Marshal.FreeCoTaskMem(absolute);
        }
    }

    private static void AppendDirectExtensionProbe(
        StringBuilder report,
        IShellFolder parent,
        IntPtr child,
        bool invokeRoot)
    {
        IntPtr dataPointer = IntPtr.Zero;
        IntPtr menu = IntPtr.Zero;
        object? extension = null;
        try
        {
            var dataObjectId = new Guid("0000010E-0000-0000-C000-000000000046");
            Marshal.ThrowExceptionForHR(parent.GetUIObjectOf(
                IntPtr.Zero,
                1,
                [child],
                ref dataObjectId,
                IntPtr.Zero,
                out dataPointer));

            var extensionType = Type.GetTypeFromCLSID(
                new Guid("D43043AF-5AD1-4B07-9571-4A8EF84F9E61"),
                true)!;
            extension = Activator.CreateInstance(extensionType)
                        ?? throw new InvalidOperationException("Cannot activate extension.");
            var initializer = (IShellExtInit)extension;
            var context = (IContextMenu3)extension;
            Marshal.ThrowExceptionForHR(initializer.Initialize(IntPtr.Zero, dataPointer, IntPtr.Zero));

            menu = CreatePopupMenu();
            var queryWatch = System.Diagnostics.Stopwatch.StartNew();
            var queryResult = context.QueryContextMenu(menu, 0, 1, 0x7fff, 0);
            queryWatch.Stop();
            Marshal.ThrowExceptionForHR(queryResult);
            report.AppendLine("DirectExtensionQueryElapsedMs=" + queryWatch.ElapsedMilliseconds);
            report.AppendLine("DirectExtensionCommandCount=" + (queryResult & 0xffff));
            var rootPosition = Find(menu, "移动到公司目录");
            report.AppendLine("DirectExtensionRootPosition=" + rootPosition);
            if (rootPosition < 0) return;
            var submenu = GetSubMenu(menu, rootPosition);
            report.AppendLine("DirectExtensionHasSubmenu=" + (submenu != IntPtr.Zero));
            if (submenu == IntPtr.Zero) return;
            context.HandleMenuMsg2(InitMenuPopup, submenu, rootPosition, out _);
            AppendMenu(report, context, submenu, 0, 2);
            if (invokeRoot)
                report.AppendLine("DirectExtensionInvokeRootHResult=" + InvokeRoot(context));
        }
        finally
        {
            if (menu != IntPtr.Zero) DestroyMenu(menu);
            if (extension is not null && Marshal.IsComObject(extension))
                Marshal.FinalReleaseComObject(extension);
            if (dataPointer != IntPtr.Zero) Marshal.Release(dataPointer);
        }
    }

    private static int InvokeRoot(IContextMenu3 context)
    {
        var info = new CommandInfo
        {
            Size = (uint)Marshal.SizeOf<CommandInfo>(),
            Verb = (IntPtr)1,
            Show = 1
        };
        var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<CommandInfo>());
        try
        {
            Marshal.StructureToPtr(info, pointer, false);
            return context.InvokeCommand(pointer);
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    private static void AppendMenu(
        StringBuilder report,
        IContextMenu3 context,
        IntPtr menu,
        int depth,
        int maximumDepth)
    {
        var count = GetMenuItemCount(menu);
        report.AppendLine($"{new string(' ', depth * 2)}Items={count}");
        for (var position = 0; position < count; position++)
        {
            var text = GetText(menu, position);
            var child = GetSubMenu(menu, position);
            report.AppendLine(
                $"{new string(' ', depth * 2)}[{position}] {text} | submenu={child != IntPtr.Zero}");

            if (child == IntPtr.Zero || depth >= maximumDepth) continue;
            context.HandleMenuMsg2(InitMenuPopup, child, position, out _);
            AppendMenu(report, context, child, depth + 1, maximumDepth);
        }
    }

    private static int Find(IntPtr menu, string expected)
    {
        var count = GetMenuItemCount(menu);
        for (var position = 0; position < count; position++)
        {
            if (GetText(menu, position).Replace("&", "") == expected) return position;
        }

        return -1;
    }

    private static string GetText(IntPtr menu, int position)
    {
        var text = new StringBuilder(512);
        GetMenuString(menu, (uint)position, text, text.Capacity, ByPosition);
        return text.ToString();
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214E6-0000-0000-C000-000000000046")]
    private interface IShellFolder
    {
        [PreserveSig] int ParseDisplayName(IntPtr a, IntPtr b, IntPtr c, ref uint d, IntPtr e, ref uint f);
        [PreserveSig] int EnumObjects(IntPtr a, uint b, out IntPtr c);
        [PreserveSig] int BindToObject(IntPtr a, IntPtr b, ref Guid c, out IntPtr d);
        [PreserveSig] int BindToStorage(IntPtr a, IntPtr b, ref Guid c, out IntPtr d);
        [PreserveSig] int CompareIDs(IntPtr a, IntPtr b, IntPtr c);
        [PreserveSig] int CreateViewObject(IntPtr a, ref Guid b, out IntPtr c);
        [PreserveSig] int GetAttributesOf(uint a, IntPtr b, ref uint c);
        [PreserveSig] int GetUIObjectOf(
            IntPtr hwnd,
            uint count,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] IntPtr[] pidls,
            ref Guid iid,
            IntPtr reserved,
            out IntPtr result);
        [PreserveSig] int GetDisplayNameOf(IntPtr a, uint b, IntPtr c);
        [PreserveSig] int SetNameOf(IntPtr a, IntPtr b, IntPtr c, uint d, out IntPtr e);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214E4-0000-0000-C000-000000000046")]
    private interface IContextMenu
    {
        [PreserveSig] int QueryContextMenu(IntPtr menu, uint index, uint first, uint last, uint flags);
        [PreserveSig] int InvokeCommand(IntPtr info);
        [PreserveSig] int GetCommandString(UIntPtr id, uint type, IntPtr reserved, IntPtr name, uint maximum);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F4-0000-0000-C000-000000000046")]
    private interface IContextMenu2
    {
        [PreserveSig] int QueryContextMenu(IntPtr menu, uint index, uint first, uint last, uint flags);
        [PreserveSig] int InvokeCommand(IntPtr info);
        [PreserveSig] int GetCommandString(UIntPtr id, uint type, IntPtr reserved, IntPtr name, uint maximum);
        [PreserveSig] int HandleMenuMsg(uint message, IntPtr wParam, IntPtr lParam);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("BCFCE0A0-EC17-11D0-8D10-00A0C90F2719")]
    private interface IContextMenu3
    {
        [PreserveSig] int QueryContextMenu(IntPtr menu, uint index, uint first, uint last, uint flags);
        [PreserveSig] int InvokeCommand(IntPtr info);
        [PreserveSig] int GetCommandString(UIntPtr id, uint type, IntPtr reserved, IntPtr name, uint maximum);
        [PreserveSig] int HandleMenuMsg(uint message, IntPtr wParam, IntPtr lParam);
        [PreserveSig] int HandleMenuMsg2(uint message, IntPtr wParam, IntPtr lParam, out IntPtr result);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214E8-0000-0000-C000-000000000046")]
    private interface IShellExtInit
    {
        [PreserveSig] int Initialize(IntPtr folder, IntPtr dataObject, IntPtr programIdKey);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CommandInfo
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

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(
        string name,
        IntPtr bindContext,
        out IntPtr pidl,
        uint inputAttributes,
        out uint outputAttributes);

    [DllImport("shell32.dll")]
    private static extern int SHBindToParent(
        IntPtr pidl,
        ref Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out IShellFolder folder,
        out IntPtr child);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    private static extern int GetMenuItemCount(IntPtr menu);

    [DllImport("user32.dll")]
    private static extern IntPtr GetSubMenu(IntPtr menu, int position);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetMenuString(
        IntPtr menu,
        uint item,
        StringBuilder text,
        int maximum,
        uint flags);
}
