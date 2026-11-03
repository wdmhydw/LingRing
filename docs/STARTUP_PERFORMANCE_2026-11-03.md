# 首次中键呼出优化（本地开发验证）

2026-11-03，基于 `e5a82c109032c2cefd944e1cd43acea78cf80948` 的 v0.6.5 源码。用户反馈首次中键后等待明显、再次打开较快。本轮优化首次呼出的准备工作，展开时长继续采用 280 ms。

## 测量结果

Windows 本机、.NET SDK 8.0.422、Release 构建；每轮启动独立探针进程，在真实资源管理器中识别临时文件，再调用实际轮盘显示代码。原版与优化版各 3 轮，优化版在首次识别前执行与应用相同的隐藏绘制和识别组件预热。

| 阶段 | 原版首次（3 轮） | 优化后首次（3 轮） |
| --- | --- | --- |
| 文件目标识别 | 387 / 354 / 414 ms | 121 / 101 / 91 ms |
| 识别开始至首次绘制 | 827 / 799 / 884 ms | 310 / 294 / 276 ms |
| 首次绘制中位数 | 827 ms | 294 ms（减少约 64%） |

优化后同一进程再次绘制为 119 / 106 / 140 ms。另两轮真实资源管理器多选验证通过，首次为 325 / 354 ms。机器负载及 Shell 提供程序会造成抖动，原版及多选测试也观察到后续调用的偶发长延迟。

计时从调用文件识别开始，截止到 WPF 调度器完成绘制且产生 Drawing；包含目标识别、窗口显示和布局绘制。它不包含鼠标硬件/原始输入消息排队，也不是 DWM 实际上屏时间或展开动画完成时间。本轮是进程首次调用测试，没有清空 Windows 文件缓存或重启电脑；数字适用于本机开发构建，不代表所有机器的保证值。

## 修改

- Shell 固定成员改为直接通过 IDispatch 反射调用，避免第一次点击时编译多组 C# `dynamic` 绑定；补齐 Folder / Document COM 引用释放。
- 优先读取 MSAA 文件项目名称，缺少名称或选中信息时再调用 UI Automation；每次点击仍读取当时的目标和选择，不缓存文件选择结果。
- 正常启动启用监听后，在 UI 空闲时创建隐藏窗口句柄、离屏准备实际轮盘字体和绘制。预热既不显示窗口，也不激活窗口；若已有点击或菜单已打开，跳过该步骤。
- 在后台初始化识别提供程序，并通过现有有界目录缓存预读默认起点的祖先链及直接子目录。读取不扩展到旁支或整个磁盘。
- 保留原有默认目录 140 ms 等待上限、动画参数、移动确认及全局中键作用范围。

## 验证与复测

- Release 构建 0 警告、0 错误；完整回归 **218/218** 通过。
- 新增隐藏窗口/焦点/目录读取检查、预热晚于真实打开时的状态保护，以及默认起点读取范围检查。
- 真实资源管理器单选与双选首次、再次识别均核对实际路径及选择数量；截图正常。验证未调用移动服务。
- 临时源文件、目录与专用资源管理器窗口在 `finally` 中清理。完整日志和绘制截图位于 `artifacts/startup-20261103/`，测试报告为 `tests/GongSiOrganizer.Core.Tests/TestResults/startup-20261103.trx`。

```powershell
$env:WINDIR = $env:SystemRoot
dotnet restore tools/StartupProbe/StartupProbe.csproj -r win-x64 --locked-mode
dotnet build tools/StartupProbe/StartupProbe.csproj -c Release --no-restore
# 真实 Explorer 测试会短暂显示专用临时窗口；与其他 UI 测试串行运行。
./scripts/Measure-StartupLatency.ps1 -OutputDirectory artifacts/startup-check -Runs 3 -WarmUp
./scripts/Measure-StartupLatency.ps1 -OutputDirectory artifacts/startup-check-multi -Runs 2 -WarmUp -SelectionCount 2
```

省略 `-WarmUp` 可测当前代码尚未预热时的路径；它不等价于修改前版本。工具只记录阶段耗时，不在 JSON 报告中记录用户文件名或选择内容。

本轮交付为本地开发版，已安装实例及公开 v0.6.5 发行基线仍保持原版本。体验本地修正时，从托盘退出旧实例，再运行 `artifacts/startup-20261103/dev/GongSiOrganizer.RadialPrototype.exe`；两个版本共用原有用户配置。
