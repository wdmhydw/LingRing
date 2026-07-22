using GongSiOrganizer.Core;

namespace GongSiOrganizer.App;

internal static class Program
{
    private const string MoveMutexName = @"Local\GongSiOrganizer.DirectMove.v3";

    [STAThread]
    private static int Main(string[] args)
    {
        var silent = args.Any(argument =>
            argument.Equals("--silent", StringComparison.OrdinalIgnoreCase));

        try
        {
            if (args.Length == 0)
                return DisableShellExtension(false);

            return args[0].ToLowerInvariant() switch
            {
                "--install" => DisableShellExtension(silent),
                "--refresh-menu" => DisableShellExtension(silent),
                "--uninstall" => UninstallMenu(silent),
                "--move" => MoveSelection(args),
                _ => ShowUsage()
            };
        }
        catch (Exception exception)
        {
            if (!silent) NativeDialog.Error(exception.Message);
            return 1;
        }
    }

    private static int DisableShellExtension(bool silent)
    {
        new ExplorerMenuInstaller().Uninstall();
        if (!silent)
            NativeDialog.Information(
                "会导致资源管理器卡死的右键扩展已停用并卸载。\n\n" +
                "本程序当前不会重新安装该扩展，也不会移动任何文件。");
        return 0;
    }

    private static int UninstallMenu(bool silent)
    {
        new ExplorerMenuInstaller().Uninstall();
        if (!silent) NativeDialog.Information("右键菜单已卸载。");
        return 0;
    }

    private static int MoveSelection(string[] args)
    {
        if (args.Length < 3)
            throw new ArgumentException("移动命令缺少目标目录或所选文件。");

        var relativePath = DestinationToken.Decode(args[1]);
        var installer = new ExplorerMenuInstaller();
        var root = installer.ReadConfiguredRoot();
        var destination = CompanyArchive.ResolveDestination(root, relativePath);
        if (!Directory.Exists(destination))
            throw new DirectoryNotFoundException($"目标目录已经不存在，请重新右键选择：{destination}");

        using var mutex = new Mutex(false, MoveMutexName);
        var acquired = false;
        try
        {
            try
            {
                acquired = mutex.WaitOne(TimeSpan.FromMinutes(10));
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            if (!acquired)
                throw new TimeoutException("另一个移动操作仍未完成，请稍后重试。");

            var mover = new DirectMoveService();
            var errors = new List<string>();

            foreach (var rawSource in args.Skip(2).Where(value => !string.IsNullOrWhiteSpace(value)))
            {
                try
                {
                    mover.Move(rawSource, destination);
                }
                catch (Exception exception)
                {
                    errors.Add($"{rawSource}\n{exception.Message}");
                }
            }

            if (errors.Count > 0)
            {
                NativeDialog.Error("以下项目未能完整移动：\n\n" + string.Join("\n\n", errors));
                return 1;
            }

            return 0;
        }
        finally
        {
            if (acquired) mutex.ReleaseMutex();
        }
    }

    private static string ReadOptionalRoot(string[] args)
    {
        var root = args.Skip(1).FirstOrDefault(argument => !argument.StartsWith("--", StringComparison.Ordinal));
        return string.IsNullOrWhiteSpace(root) ? CompanyArchive.DefaultRoot : root;
    }

    private static string GetExecutablePath() =>
        Environment.ProcessPath
        ?? throw new InvalidOperationException("无法确定程序所在位置。");

    private static int ShowUsage()
    {
        NativeDialog.Information(
            "此安全回滚版只负责停用并卸载故障右键扩展。\n\n" +
            "可用命令：\n" +
            "--uninstall");
        return 2;
    }
}
