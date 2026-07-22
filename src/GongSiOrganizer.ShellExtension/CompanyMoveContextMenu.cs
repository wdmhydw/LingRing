using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Microsoft.Win32;

namespace GongSiOrganizer.ShellExtension;

[ComVisible(true)]
[Guid(ClassId)]
[ClassInterface(ClassInterfaceType.None)]
public sealed class CompanyMoveContextMenu : IShellExtInit, IContextMenu, IContextMenu2, IContextMenu3
{
    public const string ClassId = "D43043AF-5AD1-4B07-9571-4A8EF84F9E61";

    private const int Success = 0;
    private const int NotImplemented = unchecked((int)0x80004001);
    private const int InvalidArgument = unchecked((int)0x80070057);
    private const int ReservedCommandCount = 8192;
    private const int MaximumMenuDepth = 32;
    private const string DefaultRoot = @"E:\GongSi";
    private const string SettingsPath = @"Software\GongSiMove";

    private static readonly HashSet<string> ExcludedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".SynologyWorkingDirectory",
        "$RECYCLE.BIN",
        "System Volume Information"
    };

    private readonly List<string> _selectedPaths = [];
    private readonly Dictionary<uint, string> _destinationsByOffset = [];
    private string _rootPath = DefaultRoot;
    private string? _executablePath;
    private uint _commandIdFirst;
    private uint _nextCommandOffset = 1;
    private uint _reservedCommandCount;

    public int Initialize(IntPtr folderPidl, IntPtr dataObjectPointer, IntPtr programIdKey)
    {
        _selectedPaths.Clear();
        if (dataObjectPointer == IntPtr.Zero) return Success;

        object? dataObject = null;
        try
        {
            dataObject = Marshal.GetObjectForIUnknown(dataObjectPointer);
            if (dataObject is IDataObject shellDataObject)
                _selectedPaths.AddRange(ReadSelectedPaths(shellDataObject));
        }
        catch
        {
            _selectedPaths.Clear();
        }
        finally
        {
            if (dataObject is not null && Marshal.IsComObject(dataObject))
                Marshal.ReleaseComObject(dataObject);
        }

        return Success;
    }

    public int QueryContextMenu(
        IntPtr menu,
        uint indexMenu,
        uint commandIdFirst,
        uint commandIdLast,
        uint flags)
    {
        try
        {
            if ((flags & NativeMethods.ContextMenuDefaultOnly) != 0 || _selectedPaths.Count == 0)
                return Success;

            ReadSettings();
            if (!Directory.Exists(_rootPath) ||
                string.IsNullOrWhiteSpace(_executablePath) ||
                !File.Exists(_executablePath))
                return Success;

            var available = commandIdLast >= commandIdFirst
                ? commandIdLast - commandIdFirst + 1
                : 0;
            _reservedCommandCount = Math.Min((uint)ReservedCommandCount, available);
            if (_reservedCommandCount < 16) return Success;

            _destinationsByOffset.Clear();
            _commandIdFirst = commandIdFirst;
            _nextCommandOffset = 1;

            var rootMenu = NativeMethods.CreatePopupMenu();
            if (rootMenu == IntPtr.Zero) return Marshal.GetHRForLastWin32Error();
            try
            {
                BuildMenuTree(rootMenu, _rootPath, ".", true, 0);
            }
            catch
            {
                NativeMethods.DestroyMenu(rootMenu);
                throw;
            }

            var inserted = NativeMethods.InsertMenuW(
                menu,
                indexMenu,
                NativeMethods.MenuByPosition | NativeMethods.MenuPopup | NativeMethods.MenuString,
                unchecked((UIntPtr)(ulong)rootMenu.ToInt64()),
                "移动到公司目录");
            if (!inserted)
            {
                NativeMethods.DestroyMenu(rootMenu);
                return Marshal.GetHRForLastWin32Error();
            }

            return (int)_reservedCommandCount;
        }
        catch (Exception exception)
        {
            return Marshal.GetHRForException(exception);
        }
    }

    public int InvokeCommand(IntPtr commandInfoPointer)
    {
        try
        {
            if (commandInfoPointer == IntPtr.Zero) return InvalidArgument;
            var commandInfo = Marshal.PtrToStructure<CommandInfo>(commandInfoPointer);
            var verbValue = unchecked((ulong)commandInfo.Verb.ToInt64());
            if ((verbValue >> 16) != 0) return InvalidArgument;

            var offset = (uint)(verbValue & 0xffff);
            if (!_destinationsByOffset.TryGetValue(offset, out var relativePath))
                return InvalidArgument;

            ReadSettings();
            if (string.IsNullOrWhiteSpace(_executablePath) || !File.Exists(_executablePath))
                return InvalidArgument;

            var startInfo = new ProcessStartInfo(_executablePath)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(_executablePath) ?? Environment.CurrentDirectory
            };
            startInfo.ArgumentList.Add("--move");
            startInfo.ArgumentList.Add(EncodeDestination(relativePath));
            foreach (var path in _selectedPaths) startInfo.ArgumentList.Add(path);
            Process.Start(startInfo);
            return Success;
        }
        catch (Exception exception)
        {
            return Marshal.GetHRForException(exception);
        }
    }

    public int GetCommandString(
        UIntPtr commandOffset,
        uint type,
        IntPtr reserved,
        IntPtr name,
        uint maximumCharacters) => NotImplemented;

    public int HandleMenuMsg(uint message, IntPtr wParam, IntPtr lParam)
        => Success;

    public int HandleMenuMsg2(uint message, IntPtr wParam, IntPtr lParam, out IntPtr result)
    {
        result = IntPtr.Zero;
        return Success;
    }

    private void BuildMenuTree(
        IntPtr menu,
        string fullPath,
        string relativePath,
        bool isRoot,
        int depth)
    {
        AddMoveCommand(
            menu,
            isRoot ? $"移动到 {_rootPath} 根目录" : "移动到此文件夹",
            relativePath);
        AddSeparator(menu);

        foreach (var directory in GetVisibleDirectories(fullPath))
        {
            var childRelativePath = Path.GetRelativePath(_rootPath, directory.FullName);
            var children = depth < MaximumMenuDepth
                ? GetVisibleDirectories(directory.FullName)
                : [];
            if (children.Count == 0)
            {
                AddMoveCommand(menu, directory.Name, childRelativePath);
                continue;
            }

            var childMenu = NativeMethods.CreatePopupMenu();
            if (childMenu == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            var addedToParent = false;
            try
            {
                BuildMenuTree(
                    childMenu,
                    directory.FullName,
                    childRelativePath,
                    false,
                    depth + 1);
                AddSubmenu(menu, directory.Name, childMenu);
                addedToParent = true;
            }
            finally
            {
                if (!addedToParent) NativeMethods.DestroyMenu(childMenu);
            }
        }
    }

    private void AddMoveCommand(IntPtr menu, string text, string relativePath)
    {
        if (_nextCommandOffset >= _reservedCommandCount)
        {
            AddDisabledItem(menu, "菜单项目过多，请重新右键后继续");
            return;
        }

        var offset = _nextCommandOffset++;
        var commandId = _commandIdFirst + offset;
        _destinationsByOffset[offset] = relativePath;
        if (!NativeMethods.AppendMenuW(
                menu,
                NativeMethods.MenuString,
                (UIntPtr)commandId,
                EscapeMenuText(text)))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    private static void AddSubmenu(IntPtr menu, string text, IntPtr childMenu)
    {
        if (!NativeMethods.AppendMenuW(
                menu,
                NativeMethods.MenuPopup | NativeMethods.MenuString,
                unchecked((UIntPtr)(ulong)childMenu.ToInt64()),
                EscapeMenuText(text)))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    private static void AddSeparator(IntPtr menu) =>
        NativeMethods.AppendMenuW(menu, NativeMethods.MenuSeparator, UIntPtr.Zero, null);

    private static void AddDisabledItem(IntPtr menu, string text) =>
        NativeMethods.AppendMenuW(
            menu,
            NativeMethods.MenuString | NativeMethods.MenuGrayed,
            UIntPtr.Zero,
            text);

    private static string EscapeMenuText(string text) => text.Replace("&", "&&");

    private static IReadOnlyList<DirectoryInfo> GetVisibleDirectories(string path)
    {
        try
        {
            return new DirectoryInfo(path)
                .EnumerateDirectories()
                .Where(IsVisibleDirectory)
                .OrderBy(directory => directory.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static bool IsVisibleDirectory(DirectoryInfo directory)
    {
        if (ExcludedNames.Contains(directory.Name)) return false;
        try
        {
            if ((directory.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) return false;
            return string.IsNullOrWhiteSpace(directory.LinkTarget);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void ReadSettings()
    {
        using var settings = Registry.CurrentUser.OpenSubKey(SettingsPath, false);
        var root = settings?.GetValue("RootPath") as string;
        var executable = settings?.GetValue("ExecutablePath") as string;
        _rootPath = string.IsNullOrWhiteSpace(root)
            ? DefaultRoot
            : Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        _executablePath = executable;
    }

    private static IReadOnlyList<string> ReadSelectedPaths(IDataObject dataObject)
    {
        var format = new FORMATETC
        {
            cfFormat = 15,
            dwAspect = DVASPECT.DVASPECT_CONTENT,
            lindex = -1,
            ptd = IntPtr.Zero,
            tymed = TYMED.TYMED_HGLOBAL
        };

        dataObject.GetData(ref format, out var medium);
        try
        {
            var count = NativeMethods.DragQueryFileW(medium.unionmember, uint.MaxValue, null, 0);
            var paths = new List<string>((int)count);
            for (uint index = 0; index < count; index++)
            {
                var length = NativeMethods.DragQueryFileW(medium.unionmember, index, null, 0);
                var buffer = new StringBuilder((int)length + 1);
                NativeMethods.DragQueryFileW(medium.unionmember, index, buffer, (uint)buffer.Capacity);
                if (buffer.Length > 0) paths.Add(buffer.ToString());
            }

            return paths;
        }
        finally
        {
            NativeMethods.ReleaseStgMedium(ref medium);
        }
    }

    private static string EncodeDestination(string relativePath)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(relativePath))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

}
