using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace GongSiOrganizer.RadialPrototype.Services;

internal sealed record TargetResolution(
    bool IsFileSystemItem,
    IReadOnlyList<string> ItemPaths,
    string ElementName,
    string Reason)
{
    public string? ItemPath => ItemPaths.FirstOrDefault();

    public static TargetResolution Invalid(string name, string reason) =>
        new(false, [], name, reason);
}

internal sealed class FileTargetResolver
{
    private const uint ObjidClient = 0xFFFFFFFC;
    private const int RoleSystemListItem = 0x22;
    private const int StateSystemSelected = 0x2;
    private static readonly Guid AccessibleInterfaceId =
        new("618736e0-3c3d-11cf-810c-00aa00389b71");

    public Task<TargetResolution> ResolveAsync(
        NativeScreenPoint point,
        CancellationToken cancellationToken = default) =>
        ResolveAsync(NativeDisplay.CaptureHitContext(point), cancellationToken);

    public Task<TargetResolution> ResolveAsync(
        NativeHitContext hitContext,
        CancellationToken cancellationToken = default)
    {
        var completion = new TaskCompletionSource<TargetResolution>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                completion.TrySetResult(Resolve(hitContext, cancellationToken));
            }
            catch (OperationCanceledException)
            {
                completion.TrySetCanceled(cancellationToken);
            }
            catch (Exception exception)
            {
#if DEBUG
                completion.TrySetResult(TargetResolution.Invalid(string.Empty, exception.ToString()));
#else
                completion.TrySetResult(TargetResolution.Invalid(string.Empty, exception.Message));
#endif
            }
        })
        {
            IsBackground = true,
            Name = "GongSiOrganizer.FileTargetResolver"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static TargetResolution Resolve(
        NativeHitContext hitContext,
        CancellationToken cancellationToken)
    {
        var point = hitContext.Point;
        var childWindow = hitContext.WindowHandle;
        var rootWindow = hitContext.RootWindowHandle;

        AutomationElement? element = null;
        try
        {
            element = AutomationElement.FromPoint(new System.Windows.Point(point.X, point.Y));
        }
        catch (ElementNotAvailableException)
        {
        }

        var item = FindFileListItem(element);
        var accessibleName = ResolveAccessibleListItemName(childWindow, point);
        var name = !string.IsNullOrWhiteSpace(accessibleName)
            ? accessibleName
            : item is null
                ? string.Empty
                : ReadElementName(item);
        if (string.IsNullOrWhiteSpace(name))
            return TargetResolution.Invalid(ReadElementName(element), "点击位置不是文件或文件夹项目。");

        cancellationToken.ThrowIfCancellationRequested();
        var directPath = name.Trim('"');
        var clickedPath = Exists(directPath)
            ? Path.GetFullPath(directPath)
            : ResolveUsingShell(rootWindow, name, cancellationToken)
              ?? ResolveUsingDesktopFolders(name, cancellationToken);
        if (!string.IsNullOrWhiteSpace(clickedPath) && Exists(clickedPath))
        {
            clickedPath = Path.GetFullPath(clickedPath);
            var selectedPaths = ResolveSelectedUsingShell(
                rootWindow,
                clickedPath,
                cancellationToken);
            if (selectedPaths.Count == 0)
            {
                selectedPaths = ResolveSelectedUsingAccessibility(
                    childWindow,
                    rootWindow,
                    clickedPath,
                    cancellationToken);
            }
            if (selectedPaths.Count == 0)
            {
                selectedPaths = ResolveSelectedUsingAutomation(
                    item,
                    rootWindow,
                    cancellationToken);
            }

            return new TargetResolution(
                true,
                ChooseEffectivePaths(clickedPath, selectedPaths),
                name,
                string.Empty);
        }

        return TargetResolution.Invalid(name, "无法把点击对象解析为普通文件或文件夹。");
    }

    internal static IReadOnlyList<string> ChooseEffectivePaths(
        string clickedPath,
        IEnumerable<string> selectedPaths)
    {
        var clicked = Path.GetFullPath(clickedPath);
        var selection = new List<string>();
        foreach (var selectedPath in selectedPaths)
        {
            if (string.IsNullOrWhiteSpace(selectedPath) || !Exists(selectedPath)) continue;
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(selectedPath);
            }
            catch (Exception exception) when (
                exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                continue;
            }

            if (!selection.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
                selection.Add(fullPath);
        }

        return selection.Any(path => PathsEqual(path, clicked))
            ? selection
            : [clicked];
    }

    private static string ReadElementName(AutomationElement? element)
    {
        if (element is null) return string.Empty;
        try
        {
            return NormalizeName(element.Current.Name);
        }
        catch (ElementNotAvailableException)
        {
            return string.Empty;
        }
    }

    private static AutomationElement? FindFileListItem(AutomationElement? start)
    {
        var current = start;
        for (var depth = 0; depth < 7 && current is not null; depth++)
        {
            try
            {
                var controlType = current.Current.ControlType;
                if (controlType == ControlType.ListItem || controlType == ControlType.DataItem)
                    return current;
                current = TreeWalker.ControlViewWalker.GetParent(current);
            }
            catch (ElementNotAvailableException)
            {
                return null;
            }
        }

        return null;
    }

    private static string ResolveAccessibleListItemName(
        IntPtr startWindow,
        NativeScreenPoint point)
    {
        var current = startWindow;
        for (var depth = 0; depth < 8 && current != IntPtr.Zero; depth++)
        {
            var name = ResolveAccessibleListItemNameFromWindow(current, point);
            if (!string.IsNullOrWhiteSpace(name)) return NormalizeName(name);
            current = GetParent(current);
        }

        return string.Empty;
    }

    private static string ResolveAccessibleListItemNameFromWindow(
        IntPtr window,
        NativeScreenPoint point)
    {
        object? accessibleObject = null;
        object? hit = null;
        try
        {
            var interfaceId = AccessibleInterfaceId;
            var result = AccessibleObjectFromWindow(
                window,
                ObjidClient,
                ref interfaceId,
                out accessibleObject);
            if (result < 0 || accessibleObject is not Accessibility.IAccessible accessible)
                return string.Empty;

            hit = accessible.accHitTest(point.X, point.Y);
            if (hit is null) return string.Empty;
            if (hit is Accessibility.IAccessible child)
                return ReadAccessibleListItemName(child, 0);

            var childId = Convert.ToInt32(hit);
            return childId <= 0
                ? string.Empty
                : ReadAccessibleListItemName(accessible, childId);
        }
        catch (Exception exception) when (
            exception is COMException or InvalidCastException or FormatException or OverflowException)
        {
            return string.Empty;
        }
        finally
        {
            if (hit is not null && !ReferenceEquals(hit, accessibleObject)) ReleaseCom(hit);
            ReleaseCom(accessibleObject);
        }
    }

    private static string ReadAccessibleListItemName(
        Accessibility.IAccessible accessible,
        object childId)
    {
        try
        {
            var role = Convert.ToInt32(accessible.get_accRole(childId));
            return role == RoleSystemListItem
                ? accessible.get_accName(childId) ?? string.Empty
                : string.Empty;
        }
        catch (COMException)
        {
            return string.Empty;
        }
    }

    private static string? ResolveUsingShell(
        IntPtr rootWindow,
        string displayName,
        CancellationToken cancellationToken)
    {
        var shellType = Type.GetTypeFromProgID("Shell.Application");
        if (shellType is null) return null;

        object? shell = null;
        object? windows = null;
        try
        {
            shell = Activator.CreateInstance(shellType);
            if (shell is null) return null;
            dynamic dynamicShell = shell;
            windows = dynamicShell.Windows();
            dynamic dynamicWindows = windows;
            var count = Convert.ToInt32(dynamicWindows.Count);
            for (var index = 0; index < count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                object? shellWindow = null;
                try
                {
                    shellWindow = dynamicWindows.Item(index);
                    if (shellWindow is null) continue;
                    dynamic window = shellWindow;
                    var windowHandle = new IntPtr(Convert.ToInt64(window.HWND));
                    if (rootWindow != IntPtr.Zero && windowHandle != rootWindow) continue;
                    var resolved = ResolveFromFolderItems(window.Document.Folder.Items(), displayName, cancellationToken);
                    if (resolved is not null) return resolved;
                }
                catch (COMException)
                {
                }
                finally
                {
                    ReleaseCom(shellWindow);
                }
            }

            object? desktop = null;
            try
            {
                desktop = dynamicShell.NameSpace(0);
                if (desktop is not null)
                {
                    dynamic folder = desktop;
                    return ResolveFromFolderItems(folder.Items(), displayName, cancellationToken);
                }
            }
            finally
            {
                ReleaseCom(desktop);
            }
        }
        catch (COMException)
        {
        }
        finally
        {
            ReleaseCom(windows);
            ReleaseCom(shell);
        }

        return null;
    }

    private static IReadOnlyList<string> ResolveSelectedUsingShell(
        IntPtr rootWindow,
        string clickedPath,
        CancellationToken cancellationToken)
    {
        if (rootWindow == IntPtr.Zero) return [];
        var shellType = Type.GetTypeFromProgID("Shell.Application");
        if (shellType is null) return [];

        object? shell = null;
        object? windows = null;
        try
        {
            shell = Activator.CreateInstance(shellType);
            if (shell is null) return [];
            dynamic dynamicShell = shell;
            windows = dynamicShell.Windows();
            dynamic dynamicWindows = windows;
            var count = Convert.ToInt32(dynamicWindows.Count);
            for (var index = 0; index < count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                object? shellWindow = null;
                object? document = null;
                object? selectedItems = null;
                try
                {
                    shellWindow = dynamicWindows.Item(index);
                    if (shellWindow is null) continue;
                    dynamic window = shellWindow;
                    var windowHandle = new IntPtr(Convert.ToInt64(window.HWND));
                    if (windowHandle != rootWindow) continue;

                    document = window.Document;
                    dynamic folderView = document;
                    selectedItems = folderView.SelectedItems();
                    var paths = ReadFolderItemPaths(selectedItems, cancellationToken);
                    selectedItems = null;
                    if (paths.Any(path => PathsEqual(path, clickedPath)))
                        return paths;
                }
                catch (Exception exception) when (
                    exception is COMException or InvalidCastException)
                {
                }
                finally
                {
                    ReleaseCom(selectedItems);
                    ReleaseCom(document);
                    ReleaseCom(shellWindow);
                }
            }
        }
        catch (COMException)
        {
        }
        finally
        {
            ReleaseCom(windows);
            ReleaseCom(shell);
        }

        return [];
    }

    private static IReadOnlyList<string> ResolveSelectedUsingAccessibility(
        IntPtr startWindow,
        IntPtr rootWindow,
        string clickedPath,
        CancellationToken cancellationToken)
    {
        var current = startWindow;
        for (var depth = 0; depth < 8 && current != IntPtr.Zero; depth++)
        {
            var paths = ResolveSelectedUsingAccessibilityFromWindow(
                current,
                rootWindow,
                cancellationToken);
            if (paths.Any(path => PathsEqual(path, clickedPath)))
                return paths;
            current = GetParent(current);
        }

        return [];
    }

    private static IReadOnlyList<string> ResolveSelectedUsingAccessibilityFromWindow(
        IntPtr window,
        IntPtr rootWindow,
        CancellationToken cancellationToken)
    {
        object? accessibleObject = null;
        try
        {
            var interfaceId = AccessibleInterfaceId;
            var result = AccessibleObjectFromWindow(
                window,
                ObjidClient,
                ref interfaceId,
                out accessibleObject);
            if (result < 0 || accessibleObject is not Accessibility.IAccessible accessible)
                return [];

            var paths = new List<string>();
            var childCount = Math.Clamp(accessible.accChildCount, 0, 20_000);
            for (var childId = 1; childId <= childCount; childId++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var role = Convert.ToInt32(accessible.get_accRole(childId));
                    if (role != RoleSystemListItem) continue;
                    var state = Convert.ToInt32(accessible.get_accState(childId));
                    if ((state & StateSystemSelected) == 0) continue;
                    var name = NormalizeName(accessible.get_accName(childId));
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    var path = ResolveUsingShell(rootWindow, name, cancellationToken)
                               ?? ResolveUsingDesktopFolders(name, cancellationToken);
                    if (string.IsNullOrWhiteSpace(path) || !Exists(path)) continue;
                    var fullPath = Path.GetFullPath(path);
                    if (!paths.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
                        paths.Add(fullPath);
                }
                catch (Exception exception) when (
                    exception is COMException or ArgumentException or InvalidCastException or FormatException or OverflowException)
                {
                }
            }

            return paths;
        }
        catch (COMException)
        {
            return [];
        }
        finally
        {
            ReleaseCom(accessibleObject);
        }
    }

    private static IReadOnlyList<string> ReadFolderItemPaths(
        object itemsObject,
        CancellationToken cancellationToken)
    {
        var paths = new List<string>();
        try
        {
            dynamic items = itemsObject;
            var count = Convert.ToInt32(items.Count);
            for (var index = 0; index < count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                object? itemObject = null;
                try
                {
                    itemObject = items.Item(index);
                    if (itemObject is null) continue;
                    dynamic item = itemObject;
                    string? path = Convert.ToString(item.Path);
                    if (!string.IsNullOrWhiteSpace(path) && Exists(path))
                    {
                        var fullPath = Path.GetFullPath(path);
                        if (!paths.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
                            paths.Add(fullPath);
                    }
                }
                catch (Exception exception) when (
                    exception is COMException or ArgumentException or NotSupportedException or PathTooLongException)
                {
                }
                finally
                {
                    ReleaseCom(itemObject);
                }
            }
        }
        finally
        {
            ReleaseCom(itemsObject);
        }

        return paths;
    }

    private static IReadOnlyList<string> ResolveSelectedUsingAutomation(
        AutomationElement? clickedItem,
        IntPtr rootWindow,
        CancellationToken cancellationToken)
    {
        if (clickedItem is null) return [];
        var selectedElements = ReadSelectedAutomationElements(clickedItem);
        if (selectedElements.Count == 0) return [];

        var paths = new List<string>();
        foreach (var selectedElement in selectedElements)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var selectedName = ReadElementName(selectedElement);
            if (string.IsNullOrWhiteSpace(selectedName)) continue;
            var directPath = selectedName.Trim('"');
            var path = Exists(directPath)
                ? directPath
                : ResolveUsingShell(rootWindow, selectedName, cancellationToken)
                  ?? ResolveUsingDesktopFolders(selectedName, cancellationToken);
            if (string.IsNullOrWhiteSpace(path) || !Exists(path)) continue;
            var fullPath = Path.GetFullPath(path);
            if (!paths.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
                paths.Add(fullPath);
        }

        return paths;
    }

    private static IReadOnlyList<AutomationElement> ReadSelectedAutomationElements(
        AutomationElement clickedItem)
    {
        var current = clickedItem;
        for (var depth = 0; depth < 8 && current is not null; depth++)
        {
            try
            {
                if (current.TryGetCurrentPattern(SelectionPattern.Pattern, out var patternObject) &&
                    patternObject is SelectionPattern selectionPattern)
                {
                    var selection = selectionPattern.Current.GetSelection();
                    if (selection.Length > 0) return selection;
                }

                if (current.Current.ControlType == ControlType.List ||
                    current.Current.ClassName.Equals("SysListView32", StringComparison.OrdinalIgnoreCase))
                {
                    var condition = new PropertyCondition(
                        SelectionItemPattern.IsSelectedProperty,
                        true);
                    var selection = current.FindAll(TreeScope.Descendants, condition);
                    if (selection.Count > 0)
                    {
                        var result = new AutomationElement[selection.Count];
                        for (var index = 0; index < selection.Count; index++)
                            result[index] = selection[index];
                        return result;
                    }
                }

                current = TreeWalker.ControlViewWalker.GetParent(current);
            }
            catch (Exception exception) when (
                exception is ElementNotAvailableException or InvalidOperationException)
            {
                return [];
            }
        }

        return [];
    }

    private static string? ResolveFromFolderItems(
        object itemsObject,
        string displayName,
        CancellationToken cancellationToken)
    {
        try
        {
            dynamic items = itemsObject;
            var count = Convert.ToInt32(items.Count);
            for (var index = 0; index < count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                object? itemObject = null;
                try
                {
                    itemObject = items.Item(index);
                    if (itemObject is null) continue;
                    dynamic item = itemObject;
                    var path = Convert.ToString(item.Path);
                    var name = Convert.ToString(item.Name) ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(path) &&
                        NamesMatch(displayName, name, path) &&
                        Exists(path))
                        return path;
                }
                catch (COMException)
                {
                }
                finally
                {
                    ReleaseCom(itemObject);
                }
            }
        }
        finally
        {
            ReleaseCom(itemsObject);
        }

        return null;
    }

    private static string? ResolveUsingDesktopFolders(
        string displayName,
        CancellationToken cancellationToken)
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
        };
        var oneDrive = Environment.GetEnvironmentVariable("OneDrive");
        if (!string.IsNullOrWhiteSpace(oneDrive))
        {
            candidates.Add(Path.Combine(oneDrive, "Desktop"));
            candidates.Add(Path.Combine(oneDrive, "桌面"));
        }

        foreach (var folder in candidates.Where(Directory.Exists))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                foreach (var file in Directory.EnumerateFileSystemEntries(folder))
                {
                    if (NamesMatch(displayName, Path.GetFileName(file), file)) return file;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }

        return null;
    }

    internal static bool NamesMatch(string displayName, string shellName, string path)
    {
        var normalized = NormalizeName(displayName);
        var candidates = new[]
        {
            NormalizeName(shellName),
            NormalizeName(Path.GetFileName(path)),
            NormalizeName(Path.GetFileNameWithoutExtension(path))
        };
        return candidates.Any(candidate =>
            string.Equals(normalized, candidate, StringComparison.CurrentCultureIgnoreCase) ||
            string.Equals(
                CompactName(normalized),
                CompactName(candidate),
                StringComparison.CurrentCultureIgnoreCase));
    }

    private static string NormalizeName(string? value) =>
        (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();

    private static string CompactName(string value) =>
        string.Concat(value.Where(character => !char.IsWhiteSpace(character)));

    internal static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            StringComparison.OrdinalIgnoreCase);

    private static void ReleaseCom(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            try
            {
                Marshal.FinalReleaseComObject(value);
            }
            catch (InvalidComObjectException)
            {
            }
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr window);

    [DllImport("oleacc.dll")]
    private static extern int AccessibleObjectFromWindow(
        IntPtr window,
        uint objectId,
        ref Guid interfaceId,
        [MarshalAs(UnmanagedType.Interface)] out object accessible);
}
