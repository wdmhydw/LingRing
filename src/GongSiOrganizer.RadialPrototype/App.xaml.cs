using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using GongSiOrganizer.Core;
using GongSiOrganizer.RadialPrototype.Presentation;
using GongSiOrganizer.RadialPrototype.Radial;
using GongSiOrganizer.RadialPrototype.Services;

namespace GongSiOrganizer.RadialPrototype;

public partial class App : System.Windows.Application
{
    private const string InstanceMutexName = @"Local\GongSiOrganizer.RadialPrototype.v1";

    private Mutex? _instanceMutex;
    private DirectoryTreeCache? _directoryCache;
    private RawMouseInputListener? _mouseListener;
    private FileTargetResolver? _targetResolver;
    private RadialOverlayWindow? _overlay;
    private TrayIconService? _tray;
    private CancellationTokenSource? _clickCancellation;
    private PrototypeOptions? _options;
    private readonly DirectMoveBatchService _batchMoveService = new();
    private readonly SubfolderCreationService _subfolderCreationService = new();
    private readonly FolderUsageStore _usageStore = FolderUsageStore.CreateDefault();
    private readonly ArchiveRootStore _archiveRootStore = ArchiveRootStore.CreateDefault();
    private bool _paused;
    private bool _moveInProgress;
    private bool _folderCreationInProgress;
    private bool _rootChangeInProgress;
    private bool _onboardingOpen;

    private async void OnStartup(object sender, StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        _instanceMutex = new Mutex(true, InstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            System.Windows.MessageBox.Show(
                "文件快速归档已经在运行，请查看系统托盘。",
                "文件快速归档",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        _options = PrototypeOptions.Parse(e.Args);
        CaptureTrace("options parsed");
        var startupOnboardingShown = false;
        try
        {
            if (!_options.RootWasSpecified)
            {
                var storedRoot = _archiveRootStore.Load();
                if (!string.IsNullOrWhiteSpace(storedRoot))
                    _options = _options with { RootPath = storedRoot };
            }

            if (!Directory.Exists(_options.RootPath))
            {
                startupOnboardingShown = true;
                var startAtRootPage = _archiveRootStore.IsOnboardingCompleted();
                if (!ShowRequiredRootOnboarding(startAtRootPage))
                {
                    Shutdown();
                    return;
                }
            }

            if (_directoryCache is null)
            {
                _directoryCache = new DirectoryTreeCache(_options.RootPath);
                CaptureTrace("directory scan started");
                await _directoryCache.ReloadAsync();
                CaptureTrace("directory scan completed");
            }
            _targetResolver = new FileTargetResolver();

            if (!string.IsNullOrWhiteSpace(_options.SelectionProbePath))
            {
                var resolution = await _targetResolver.ResolveAsync(
                    NativeDisplay.CaptureHitContext());
                var outputDirectory = Path.GetDirectoryName(_options.SelectionProbePath);
                if (!string.IsNullOrWhiteSpace(outputDirectory))
                    Directory.CreateDirectory(outputDirectory);
                File.WriteAllLines(
                    _options.SelectionProbePath,
                    resolution.IsFileSystemItem
                        ? resolution.ItemPaths
                        : [$"ERROR: {resolution.Reason}"]);
                Shutdown();
                return;
            }

            _overlay = new RadialOverlayWindow();
            _overlay.MoveRequested += OnMoveRequested;
            _overlay.DemoConfirmed += OnDemoConfirmed;
            _overlay.CreateFolderRequested += OnCreateFolderRequested;
            CaptureTrace("overlay created");

            _tray = new TrayIconService(
                ShowDemoAtCursor,
                RefreshDirectory,
                ChangeArchiveRoot,
                ResetUsageRanking,
                ShowOnboarding,
                paused => _paused = paused,
                RequestShutdown,
                _options.RootPath);
            CaptureTrace("tray created");

            if (_options.DemoTrayMenu)
            {
                _tray.ShowMenuAt(NativeDisplay.GetPrimaryWorkAreaCenter());
                if (!string.IsNullOrWhiteSpace(_options.CapturePath))
                {
                    await Task.Delay(_options.CaptureDelayMilliseconds);
                    await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                    _tray.SaveMenuSnapshot(_options.CapturePath);
                    Shutdown();
                }
                return;
            }

            if (_options.DemoOnboarding)
            {
                var demoGuide = new OnboardingWindow(
                    _options.RootPath,
                    ChooseArchiveRootFromOnboardingAsync);
                demoGuide.Show();
                demoGuide.ShowPageForPreview(_options.DemoOnboardingPage);
                if (!string.IsNullOrWhiteSpace(_options.CapturePath))
                {
                    await Task.Delay(_options.CaptureDelayMilliseconds);
                    await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                    demoGuide.SaveSnapshot(_options.CapturePath);
                    demoGuide.Close();
                    Shutdown();
                }
                return;
            }

            if (!_options.DisableListener)
            {
                _mouseListener = new RawMouseInputListener();
                _mouseListener.MiddleButtonDown += OnMiddleButtonDown;
                _mouseListener.LeftButtonDown += OnGlobalLeftButtonDown;
                _mouseListener.Start();
            }

            if (!_options.IsDemoLaunch &&
                !startupOnboardingShown &&
                !_archiveRootStore.IsOnboardingCompleted())
                ShowOnboarding();

            if (Process.GetProcessesByName("Listary").Length > 0)
            {
                _tray.ShowWarning(
                    "检测到 Listary",
                    "测试前请在 Listary 的“选项 → 集成”中关闭 Desktop 和 File Explorer 的鼠标中键菜单。");
            }

            if (_options.DemoToast)
            {
                _tray.ShowStatus(
                    "目录已刷新",
                    "当前缓存 128 个目录。此提示不会进入通知中心。",
                    5000);
            }

            if (_options.DemoInvalid)
            {
                _overlay.ShowInvalid(NativeDisplay.GetPrimaryWorkAreaCenter(_options.DemoCorner));
            }
            else if (_options.Demo || _options.DemoCorner)
            {
                CaptureTrace("demo opening");
                ShowDemo(NativeDisplay.GetPrimaryWorkAreaCenter(_options.DemoCorner));
                CaptureTrace($"demo opened; visible={_overlay.IsVisible}");
            }

            if (_options.DemoHoverRing >= 0 && _overlay.IsVisible)
                _overlay.HoverDemo(_options.DemoHoverRing);

            if ((_options.DemoScroll || _options.DemoScrollTwice) && _overlay.IsVisible)
            {
                await Task.Delay(300);
                _overlay.RotateDemo(1);
                if (_options.DemoScrollTwice)
                {
                    await Task.Delay(65);
                    _overlay.RotateDemo(1);
                }
            }

            if (_options.DemoFolderDialog && _overlay.IsVisible)
            {
                _overlay.PromptForFolderName(
                    @"安琪儿\产科",
                    _ => "界面验收模式不会创建文件夹。");
            }

            if (!string.IsNullOrWhiteSpace(_options.CapturePath) && _overlay.IsVisible)
            {
                CaptureTrace("capture delay started");
                await Task.Delay(_options.CaptureDelayMilliseconds);
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                _overlay.SaveSnapshot(_options.CapturePath);
                CaptureTrace("capture saved");
                Shutdown();
            }
        }
        catch (Exception exception)
        {
            CaptureTrace("startup failed: " + exception);
            System.Windows.MessageBox.Show(
                exception.Message,
                "文件快速归档启动失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
        }
    }

    private void OnMiddleButtonDown(NativeHitContext hitContext)
    {
        if (_paused || _folderCreationInProgress || _rootChangeInProgress || _onboardingOpen ||
            _targetResolver is null || _overlay is null) return;
        if (!ShellMiddleClickScope.IsSupported(hitContext)) return;
        _ = Dispatcher.InvokeAsync(async () =>
        {
            _clickCancellation?.Cancel();
            _clickCancellation?.Dispose();
            _clickCancellation = new CancellationTokenSource();
            var token = _clickCancellation.Token;
            try
            {
                var resolution = await _targetResolver.ResolveAsync(hitContext, token);
                if (token.IsCancellationRequested) return;
                var snapshot = _directoryCache?.Snapshot;
                if (resolution.IsFileSystemItem && resolution.ItemPaths.Count > 0 && snapshot is not null)
                    _overlay.ShowMenu(
                        hitContext.Point,
                        resolution.ItemPaths,
                        _usageStore.Apply(snapshot));
                else
                    _overlay.ShowInvalid(hitContext.Point);
            }
            catch (OperationCanceledException)
            {
            }
        }, DispatcherPriority.Input);
    }

    private void OnGlobalLeftButtonDown(NativeScreenPoint point)
    {
        var overlay = _overlay;
        if (_folderCreationInProgress || overlay is null || !overlay.IsVisible) return;
        if (!overlay.ShouldDismissForGlobalClick(point)) return;
        _ = Dispatcher.InvokeAsync(overlay.HideOverlay, DispatcherPriority.Input);
    }

    private void ShowDemoAtCursor()
    {
        if (_folderCreationInProgress || _rootChangeInProgress || _onboardingOpen) return;
        try
        {
            ShowDemo(NativeDisplay.GetCursor());
        }
        catch (Exception exception)
        {
            _tray?.ShowWarning("无法显示演示", exception.Message);
        }
    }

    private void ShowDemo(NativeScreenPoint point)
    {
        var snapshot = _directoryCache?.Snapshot;
        if (snapshot is null || _overlay is null) return;
        _overlay.ShowMenu(point, ["演示文件.pptx"], _usageStore.Apply(snapshot), true);
    }

    private async void RefreshDirectory()
    {
        if (_rootChangeInProgress || _directoryCache is null) return;
        try
        {
            await _directoryCache.ReloadAsync();
            _tray?.ShowStatus(
                "目录已刷新",
                $"当前缓存 {_directoryCache.Snapshot?.FolderCount ?? 0:N0} 个目录。");
        }
        catch (Exception exception)
        {
            _tray?.ShowWarning("目录刷新失败", exception.Message);
        }
    }

    private async void OnMoveRequested(
        IReadOnlyList<string> sourceFiles,
        string relativeDestination)
    {
        var options = _options;
        if (options is null || sourceFiles.Count == 0) return;
        if (_rootChangeInProgress)
        {
            _tray?.ShowWarning("正在切换归档目录", "目录读取完成后再移动文件。");
            return;
        }
        if (_moveInProgress)
        {
            _tray?.ShowWarning("已有移动正在进行", "请等待当前文件移动完成后再操作。");
            return;
        }

        _moveInProgress = true;
        var itemCount = sourceFiles.Count;
        var sourceLabel = itemCount == 1
            ? Path.GetFileName(sourceFiles[0])
            : $"已选择 {itemCount} 项";
        try
        {
            _tray?.SetBusyStatus($"正在移动 {itemCount} 项…");

            DirectMoveBatchResult result;
            try
            {
                result = await Task.Run(() => _batchMoveService.MoveToArchive(
                    sourceFiles,
                    options.RootPath,
                    relativeDestination));
            }
            catch (Exception exception)
            {
                _tray?.ShowWarning(
                    "移动失败",
                    $"{sourceLabel}\n{exception.Message}",
                    6000);
                return;
            }

            if (result.Moved.Count > 0)
                _usageStore.RecordSelection(options.RootPath, relativeDestination);

            if (result.Failures.Count == 0)
            {
                var destinationLabel = result.Moved.Count == 1
                    ? Path.GetRelativePath(options.RootPath, result.Moved[0].DestinationPath)
                    : relativeDestination;
                _tray?.ShowMoveSuccess(
                    "移动完成",
                    $"已移动 {result.Moved.Count} 项  →  {destinationLabel}");
            }
            else
            {
                var firstFailure = result.Failures[0];
                var extraFailureText = result.Failures.Count > 1
                    ? $"\n另有 {result.Failures.Count - 1} 项失败"
                    : string.Empty;
                _tray?.ShowWarning(
                    result.Moved.Count > 0 ? "部分移动完成" : "移动失败",
                    $"已移动 {result.Moved.Count}/{result.TotalCount} 项\n" +
                    $"{Path.GetFileName(firstFailure.SourcePath)}：{firstFailure.Message}" +
                    extraFailureText,
                    6500);
            }

            if (result.Moved.Any(item => item.WasDirectory) && _directoryCache is not null)
            {
                try
                {
                    await _directoryCache.ReloadAsync();
                }
                catch (Exception exception)
                {
                    _tray?.ShowWarning(
                        "文件夹已移动，但目录刷新失败",
                        exception.Message);
                }
            }
        }
        finally
        {
            _tray?.SetBusyStatus(null);
            _moveInProgress = false;
        }
    }

    private void OnDemoConfirmed(string relativeDestination)
    {
        _tray?.ShowStatus(
            "演示模式：未移动文件",
            $"演示目标：{relativeDestination}",
            3000);
    }

    private async void OnCreateFolderRequested(string parentRelativePath, bool isDemo)
    {
        var options = _options;
        var overlay = _overlay;
        var directoryCache = _directoryCache;
        if (options is null || overlay is null || directoryCache is null) return;
        if (_rootChangeInProgress) return;
        if (isDemo)
        {
            _tray?.ShowStatus(
                "安全演示不会新建文件夹",
                "请在真实文件或文件夹上打开轮盘后使用 Ctrl + 左键。");
            return;
        }

        if (_folderCreationInProgress) return;
        _folderCreationInProgress = true;
        SubfolderCreationResult? created = null;
        try
        {
            var accepted = overlay.PromptForFolderName(
                parentRelativePath,
                requestedName =>
                {
                    try
                    {
                        created = _subfolderCreationService.Create(
                            options.RootPath,
                            parentRelativePath,
                            requestedName);
                        return null;
                    }
                    catch (Exception exception) when (
                        exception is ArgumentException or
                            IOException or
                            UnauthorizedAccessException or
                            NotSupportedException or
                            System.Security.SecurityException)
                    {
                        return exception.Message;
                    }
                });
            if (!accepted || created is null) return;

            overlay.SetInteractionEnabled(false);
            try
            {
                await directoryCache.ReloadAsync();
                var snapshot = directoryCache.Snapshot;
                if (snapshot is not null)
                    overlay.RefreshMenu(_usageStore.Apply(snapshot), created.RelativePath);
            }
            catch (Exception exception)
            {
                overlay.HideOverlay();
                _tray?.ShowWarning(
                    "文件夹已创建，但目录刷新失败",
                    exception.Message);
            }
            finally
            {
                overlay.SetInteractionEnabled(true);
            }
        }
        finally
        {
            _folderCreationInProgress = false;
        }
    }

    private void ResetUsageRanking()
    {
        var rootPath = _options?.RootPath;
        if (string.IsNullOrWhiteSpace(rootPath)) return;
        _usageStore.Clear(rootPath);
        _tray?.ShowStatus(
            "常用排序已重置",
            "当前归档根目录将根据新的移动选择重新累计。");
    }

    private async void ChangeArchiveRoot()
    {
        var currentOptions = _options;
        if (currentOptions is null || _rootChangeInProgress || _onboardingOpen) return;
        if (_moveInProgress || _folderCreationInProgress)
        {
            _tray?.ShowWarning(
                "暂时无法更改归档目录",
                "请等待当前文件操作完成后再试。");
            return;
        }

        _rootChangeInProgress = true;
        try
        {
            _clickCancellation?.Cancel();
            _overlay?.HideOverlay();
            var selectedRoot = ArchiveRootPicker.Pick(currentOptions.RootPath);
            if (string.IsNullOrWhiteSpace(selectedRoot) ||
                selectedRoot.Equals(currentOptions.RootPath, StringComparison.OrdinalIgnoreCase))
                return;

            _tray?.SetBusyStatus("正在读取新的归档目录…");
            var result = await ApplyArchiveRootAsync(selectedRoot);
            if (!result.Success)
            {
                _tray?.ShowWarning(
                    "无法更改归档目录",
                    result.Error ?? "读取目录时发生未知错误。",
                    6000);
            }
            else if (result.SettingsSaved)
            {
                _tray?.ShowStatus(
                    "归档根目录已更改",
                    $"已读取 {result.FolderCount:N0} 个子目录。",
                    3000);
            }
            else
            {
                _tray?.ShowWarning(
                    "根目录已切换，但设置未保存",
                    "本次运行可以正常使用；下次启动时可能需要重新选择。",
                    6000);
            }
        }
        catch (Exception exception)
        {
            _tray?.ShowWarning(
                "无法更改归档目录",
                exception.Message,
                6000);
        }
        finally
        {
            _tray?.SetBusyStatus(null);
            _rootChangeInProgress = false;
        }
    }

    private async Task<ArchiveRootApplyResult> ApplyArchiveRootAsync(string selectedRoot)
    {
        var currentOptions = _options;
        if (currentOptions is null)
            return new ArchiveRootApplyResult(false, selectedRoot, 0, false, "程序尚未完成初始化。");

        DirectoryTreeCache? replacement = null;
        try
        {
            var normalizedRoot = CompanyArchive.NormalizeRoot(selectedRoot);
            if (normalizedRoot.Equals(currentOptions.RootPath, StringComparison.OrdinalIgnoreCase) &&
                _directoryCache?.Snapshot is not null)
            {
                var currentCount = _directoryCache?.Snapshot?.FolderCount ?? 0;
                return new ArchiveRootApplyResult(
                    true,
                    normalizedRoot,
                    currentCount,
                    _archiveRootStore.Save(normalizedRoot),
                    null);
            }

            replacement = new DirectoryTreeCache(normalizedRoot);
            await replacement.ReloadAsync();

            var previous = _directoryCache;
            _directoryCache = replacement;
            replacement = null;
            _options = currentOptions with { RootPath = normalizedRoot };
            _tray?.SetArchiveRoot(normalizedRoot);
            try
            {
                previous?.Dispose();
            }
            catch
            {
                // The replacement cache is already active; disposal must not undo a valid switch.
            }

            return new ArchiveRootApplyResult(
                true,
                normalizedRoot,
                _directoryCache.Snapshot?.FolderCount ?? 0,
                _archiveRootStore.Save(normalizedRoot),
                null);
        }
        catch (Exception exception)
        {
            return new ArchiveRootApplyResult(false, currentOptions.RootPath, 0, false, exception.Message);
        }
        finally
        {
            replacement?.Dispose();
        }
    }

    private async Task<OnboardingRootSelectionResult> ChooseArchiveRootFromOnboardingAsync(
        string currentRoot)
    {
        if (_rootChangeInProgress)
        {
            return new OnboardingRootSelectionResult(
                false,
                false,
                currentRoot,
                "正在读取另一个目录，请稍后再试");
        }

        if (_moveInProgress || _folderCreationInProgress)
        {
            return new OnboardingRootSelectionResult(
                false,
                false,
                currentRoot,
                "请等待当前文件操作完成后再选择目录");
        }

        _rootChangeInProgress = true;
        try
        {
            _clickCancellation?.Cancel();
            _overlay?.HideOverlay();
            var selectedRoot = ArchiveRootPicker.Pick(currentRoot);
            if (string.IsNullOrWhiteSpace(selectedRoot))
                return new OnboardingRootSelectionResult(false, true, currentRoot, null);

            _tray?.SetBusyStatus("正在读取新的归档目录…");
            var result = await ApplyArchiveRootAsync(selectedRoot);
            if (!result.Success)
            {
                return new OnboardingRootSelectionResult(
                    false,
                    false,
                    currentRoot,
                    $"读取失败：{result.Error ?? "未知错误"}");
            }

            var message = result.SettingsSaved
                ? $"设置成功，已读取 {result.FolderCount:N0} 个子目录"
                : "目录已切换，但设置暂未保存";
            return new OnboardingRootSelectionResult(
                true,
                false,
                result.RootPath,
                message);
        }
        catch (Exception exception)
        {
            return new OnboardingRootSelectionResult(
                false,
                false,
                currentRoot,
                $"设置失败：{exception.Message}");
        }
        finally
        {
            _tray?.SetBusyStatus(null);
            _rootChangeInProgress = false;
        }
    }

    private bool ShowRequiredRootOnboarding(bool startAtRootPage)
    {
        var options = _options;
        if (options is null || _onboardingOpen) return false;

        _onboardingOpen = true;
        try
        {
            var guide = new OnboardingWindow(
                options.RootPath,
                ChooseArchiveRootFromOnboardingAsync,
                rootSelectionRequired: true);
            if (startAtRootPage) guide.ShowPageForPreview(3);
            _ = guide.ShowDialog();

            var activeRoot = _options?.RootPath;
            if (guide.Completed)
                _ = _archiveRootStore.MarkOnboardingCompleted(activeRoot);
            return !string.IsNullOrWhiteSpace(activeRoot) && Directory.Exists(activeRoot);
        }
        finally
        {
            _onboardingOpen = false;
        }
    }

    private void ShowOnboarding()
    {
        var options = _options;
        if (options is null || _onboardingOpen) return;
        _onboardingOpen = true;
        _clickCancellation?.Cancel();
        _overlay?.HideOverlay();
        try
        {
            var guide = new OnboardingWindow(
                options.RootPath,
                ChooseArchiveRootFromOnboardingAsync);
            _ = guide.ShowDialog();
            if (guide.Completed && !_archiveRootStore.MarkOnboardingCompleted(_options?.RootPath))
            {
                _tray?.ShowWarning(
                    "引导状态未能保存",
                    "不影响使用，但下次启动时可能再次显示新手引导。");
            }
        }
        finally
        {
            _onboardingOpen = false;
        }
    }

    private void RequestShutdown()
    {
        if (_onboardingOpen)
        {
            _tray?.ShowWarning("新手引导正在显示", "关闭引导窗口后再退出。");
            return;
        }
        if (_rootChangeInProgress)
        {
            _tray?.ShowWarning("正在切换归档目录", "目录读取完成后再退出。");
            return;
        }

        if (_folderCreationInProgress)
        {
            _tray?.ShowWarning("正在新建文件夹", "完成命名或取消后再退出。");
            return;
        }

        if (_moveInProgress)
        {
            _tray?.ShowWarning("文件正在移动", "移动完成后才能退出，避免文件操作中断。");
            return;
        }

        Shutdown();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _tray?.ShowWarning("文件快速归档发生错误", e.Exception.Message);
        e.Handled = true;
    }

    private void OnExit(object sender, ExitEventArgs e)
    {
        _clickCancellation?.Cancel();
        _clickCancellation?.Dispose();
        if (_mouseListener is not null)
        {
            _mouseListener.MiddleButtonDown -= OnMiddleButtonDown;
            _mouseListener.LeftButtonDown -= OnGlobalLeftButtonDown;
        }
        _mouseListener?.Dispose();
        if (_overlay is not null)
        {
            _overlay.MoveRequested -= OnMoveRequested;
            _overlay.DemoConfirmed -= OnDemoConfirmed;
            _overlay.CreateFolderRequested -= OnCreateFolderRequested;
        }
        _overlay?.Close();
        _tray?.Dispose();
        _directoryCache?.Dispose();
        _instanceMutex?.Dispose();
    }

    private void CaptureTrace(string message)
    {
        var capturePath = _options?.CapturePath;
        if (string.IsNullOrWhiteSpace(capturePath)) return;
        try
        {
            File.AppendAllText(
                capturePath + ".trace.txt",
                $"{DateTime.Now:HH:mm:ss.fff}\t{message}{Environment.NewLine}");
        }
        catch
        {
            // Diagnostic output must never affect the actual app path.
        }
    }

    private sealed record ArchiveRootApplyResult(
        bool Success,
        string RootPath,
        int FolderCount,
        bool SettingsSaved,
        string? Error);
}
