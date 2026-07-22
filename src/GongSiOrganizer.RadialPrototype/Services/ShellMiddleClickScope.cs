using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace GongSiOrganizer.RadialPrototype.Services;

internal static class ShellMiddleClickScope
{
    private const int MaximumAncestorDepth = 18;

    public static bool IsSupported(NativeHitContext hitContext)
    {
        if (hitContext.WindowHandle == IntPtr.Zero) return false;
        var rootWindow = hitContext.RootWindowHandle != IntPtr.Zero
            ? hitContext.RootWindowHandle
            : hitContext.WindowHandle;
        var processName = ReadProcessName(rootWindow);
        if (!processName.Equals("explorer", StringComparison.OrdinalIgnoreCase)) return false;

        var classes = new List<string>();
        var current = hitContext.WindowHandle;
        for (var depth = 0; depth < MaximumAncestorDepth && current != IntPtr.Zero; depth++)
        {
            var className = ReadClassName(current);
            if (!string.IsNullOrWhiteSpace(className)) classes.Add(className);
            if (current == rootWindow) break;
            current = GetParent(current);
        }

        return IsSupportedHierarchy(processName, classes);
    }

    internal static bool IsSupportedHierarchy(
        string? processName,
        IEnumerable<string> windowClasses)
    {
        if (!string.Equals(processName, "explorer", StringComparison.OrdinalIgnoreCase))
            return false;

        return windowClasses.Any(className =>
            className.Equals("SHELLDLL_DefView", StringComparison.OrdinalIgnoreCase) ||
            className.Equals("SysListView32", StringComparison.OrdinalIgnoreCase));
    }

    private static string ReadProcessName(IntPtr window)
    {
        try
        {
            _ = GetWindowThreadProcessId(window, out var processId);
            if (processId == 0) return string.Empty;
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return string.Empty;
        }
    }

    private static string ReadClassName(IntPtr window)
    {
        var buffer = new StringBuilder(256);
        return GetClassName(window, buffer, buffer.Capacity) > 0
            ? buffer.ToString()
            : string.Empty;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder className, int maximumCount);

    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr window);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}
