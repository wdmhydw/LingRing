using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace GongSiOrganizer.Core;

public sealed record MenuInstallResult(
    string RootPath,
    int FolderCount,
    int SkippedCount);

public sealed class ExplorerMenuInstaller
{
    public const string ShellExtensionClassId = "{D43043AF-5AD1-4B07-9571-4A8EF84F9E61}";

    private const string SettingsRegistryPath = @"Software\GongSiMove";
    private const string ContextMenuHandlerRegistryPath =
        @"Software\Classes\AllFilesystemObjects\shellex\ContextMenuHandlers\GongSiMoveV4";
    private const string ComClassRegistryPath =
        @"Software\Classes\CLSID\" + ShellExtensionClassId;
    private const string ApprovedExtensionsRegistryPath =
        @"Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Approved";
    private const string StaticMenuRegistryPath =
        @"Software\Classes\AllFilesystemObjects\shell\GongSiMove";
    private const string StaticMenuStoreRegistryPath = @"Software\Classes\GongSiMove.Menu";
    private const string MenuMutexName = @"Local\GongSiOrganizer.MenuInstall.v3";
    private const string ShellExtensionAssemblyName = "GongSiOrganizer.ShellExtension.V4";

    private static readonly string[] RegisteredShellExtensionClassIds =
    [
        ShellExtensionClassId,
        "{B8267A72-98B7-4D6B-8EA5-D56F5D8BA2E4}",
        "{D2D31C32-6027-4E95-8A3A-7B6828F0EF72}"
    ];

    private static readonly string[] LegacyContextMenuHandlerRegistryPaths =
    [
        @"Software\Classes\AllFilesystemObjects\shellex\ContextMenuHandlers\GongSiMove"
    ];

    private static readonly string[] RequiredShellExtensionSuffixes =
    [
        ".comhost.dll",
        ".dll",
        ".deps.json",
        ".runtimeconfig.json"
    ];

    private static readonly string[] LegacyMenuRegistryPaths =
    [
        @"Software\Classes\*\shell\GongSiOrganizer",
        @"Software\Classes\Directory\shell\GongSiOrganizer",
        @"Software\Classes\AllFilesystemObjects\shell\GongSiOrganizer",
        @"Software\Classes\*\shell\GongSiMove",
        @"Software\Classes\Directory\shell\GongSiMove",
        StaticMenuRegistryPath,
        StaticMenuStoreRegistryPath
    ];

    private readonly FolderCatalog _catalog = new();

    public MenuInstallResult Install(string rootPath, string executablePath) =>
        WithMenuLock(() => InstallCore(rootPath, executablePath));

    private MenuInstallResult InstallCore(string rootPath, string executablePath)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("右键菜单只能安装在 Windows 上。");
        if (!Environment.Is64BitProcess)
            throw new PlatformNotSupportedException("请使用 win-x64 版本安装右键菜单。");

        var executable = Path.GetFullPath(executablePath);
        if (!File.Exists(executable))
            throw new FileNotFoundException("找不到右键菜单执行程序。", executable);

        var extensionFiles = GetShellExtensionFilePaths(executable);
        foreach (var file in extensionFiles)
        {
            if (!File.Exists(file))
            {
                throw new FileNotFoundException(
                    "安装包不完整：缺少动态右键菜单组件。请保留 exe 旁边的 shell 文件夹。",
                    file);
            }
        }

        // 扫描仅用于安装结果统计。菜单本身会在每次悬停时读取当前目录，不保存快照。
        var catalog = _catalog.Scan(rootPath);
        var comHostPath = GetShellExtensionHostPath(executable);

        RemoveLegacyIntegrations();
        RemoveDynamicRegistration();

        try
        {
            WriteSettings(catalog.RootPath, executable, catalog.FolderCount);

            using (var classKey = Registry.CurrentUser.CreateSubKey(ComClassRegistryPath, true)
                                  ?? throw new InvalidOperationException("无法注册右键菜单组件。"))
            {
                classKey.SetValue(null, "公司文件一键移动", RegistryValueKind.String);
                using var server = classKey.CreateSubKey("InprocServer32", true)
                                   ?? throw new InvalidOperationException("无法注册右键菜单组件入口。");
                server.SetValue(null, comHostPath, RegistryValueKind.String);
                server.SetValue("ThreadingModel", "Both", RegistryValueKind.String);
            }

            using (var handler = Registry.CurrentUser.CreateSubKey(
                       ContextMenuHandlerRegistryPath,
                       true)
                   ?? throw new InvalidOperationException("无法写入资源管理器右键菜单设置。"))
            {
                handler.SetValue(null, ShellExtensionClassId, RegistryValueKind.String);
            }

            using (var approved = Registry.CurrentUser.CreateSubKey(
                       ApprovedExtensionsRegistryPath,
                       true)
                   ?? throw new InvalidOperationException("无法批准右键菜单组件。"))
            {
                approved.SetValue(
                    ShellExtensionClassId,
                    "公司文件一键移动",
                    RegistryValueKind.String);
            }
        }
        catch
        {
            RemoveDynamicRegistration();
            throw;
        }

        NotifyShellAssociationsChanged();
        return new MenuInstallResult(catalog.RootPath, catalog.FolderCount, catalog.SkippedCount);
    }

    public string ReadConfiguredRoot()
    {
        using var key = Registry.CurrentUser.OpenSubKey(SettingsRegistryPath, false);
        var configured = key?.GetValue("RootPath") as string;
        return string.IsNullOrWhiteSpace(configured)
            ? CompanyArchive.DefaultRoot
            : CompanyArchive.NormalizeRoot(configured);
    }

    public void Uninstall()
    {
        WithMenuLock(() =>
        {
            RemoveDynamicRegistration();
            RemoveLegacyIntegrations();
            Registry.CurrentUser.DeleteSubKeyTree(SettingsRegistryPath, false);
            NotifyShellAssociationsChanged();
            return true;
        });
    }

    public static string GetShellExtensionHostPath(string executablePath) =>
        GetShellExtensionFilePaths(executablePath)[0];

    public static IReadOnlyList<string> GetShellExtensionFilePaths(string executablePath)
    {
        var executable = Path.GetFullPath(executablePath);
        var parent = Path.GetDirectoryName(executable)
                     ?? throw new ArgumentException("执行程序路径没有父目录。", nameof(executablePath));
        var shellDirectory = Path.Combine(parent, "shell");
        return RequiredShellExtensionSuffixes
            .Select(suffix => Path.Combine(shellDirectory, ShellExtensionAssemblyName + suffix))
            .ToArray();
    }

    private static void WriteSettings(string rootPath, string executablePath, int folderCount)
    {
        using var settings = Registry.CurrentUser.CreateSubKey(SettingsRegistryPath, true)
                             ?? throw new InvalidOperationException("无法保存右键菜单设置。");
        settings.SetValue("RootPath", rootPath, RegistryValueKind.String);
        settings.SetValue("ExecutablePath", executablePath, RegistryValueKind.String);
        settings.SetValue("FolderCount", folderCount, RegistryValueKind.DWord);
        settings.SetValue("Version", "0.4.0", RegistryValueKind.String);
    }

    private static void RemoveDynamicRegistration()
    {
        Registry.CurrentUser.DeleteSubKeyTree(ContextMenuHandlerRegistryPath, false);
        foreach (var path in LegacyContextMenuHandlerRegistryPaths)
            Registry.CurrentUser.DeleteSubKeyTree(path, false);

        using var approved = Registry.CurrentUser.OpenSubKey(ApprovedExtensionsRegistryPath, true);
        foreach (var classId in RegisteredShellExtensionClassIds)
        {
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\CLSID\" + classId, false);
            approved?.DeleteValue(classId, false);
        }
    }

    private static void RemoveLegacyIntegrations()
    {
        foreach (var path in LegacyMenuRegistryPaths)
            Registry.CurrentUser.DeleteSubKeyTree(path, false);

        using (var run = Registry.CurrentUser.OpenSubKey(
                   @"Software\Microsoft\Windows\CurrentVersion\Run",
                   true))
        {
            run?.DeleteValue("GongSiOrganizer", false);
            run?.DeleteValue("GongSiMove", false);
        }

        var sendTo = Environment.GetFolderPath(Environment.SpecialFolder.SendTo);
        foreach (var fileName in new[]
                 {
                     "公司文件整理（可多选）.lnk",
                     "公司文件整理(可多选).lnk",
                     "GongSiOrganizer.lnk"
                 })
        {
            var path = Path.Combine(sendTo, fileName);
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // 旧快捷方式清理失败不影响新的右键菜单。
            }
        }
    }

    private static T WithMenuLock<T>(Func<T> action)
    {
        using var mutex = new Mutex(false, MenuMutexName);
        var acquired = false;
        try
        {
            try
            {
                acquired = mutex.WaitOne(TimeSpan.FromMinutes(5));
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            if (!acquired)
                throw new TimeoutException("右键菜单正在由另一个进程安装，请稍后重试。");

            return action();
        }
        finally
        {
            if (acquired) mutex.ReleaseMutex();
        }
    }

    private static void NotifyShellAssociationsChanged() =>
        SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(
        uint eventId,
        uint flags,
        IntPtr item1,
        IntPtr item2);
}
