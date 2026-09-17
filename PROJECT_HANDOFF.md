> 2026-09-17 发布补充：用户明确授权上传 GitHub 与私人 Forgejo 仓库，要求更新 README 与截图脱敏，并确认无需激活授权。下文历史限制不覆盖此次授权；当前进度见 docs/PROJECT_STATE.md。

> 2026-09-17 更新：用户已批准蓝弧方案并要求制作可试用成品。下文是 0.5.5 的历史审查交接，其中“只读、不改源码、不打包”不再代表当前授权。当前状态见 [docs/PROJECT_STATE.md](docs/PROJECT_STATE.md)，验证与构建证据见 [docs/ACCEPTANCE_LOG.md](docs/ACCEPTANCE_LOG.md)。旧运行实例与用户数据仍受保护，未自动替换。

# PROJECT_HANDOFF — 文件快速归档 v0.5.5 优化审查

## 0. Snapshot / 快照

- Updated: `2026-08-19T09:40:04+08:00`
- Project root: `E:\YingYong\Agent\自动桌面整理`
- Repository: `https://github.com/wdmhydw/LingRing`（2026-09-15 由 FileQuickArchive 重命名）
- Previous agent/model: `Codex`
- Intended next agent: `更强的代码审查/架构模型`
- Branch/worktree: `main`; 本轮开始时干净，当前只新增本交接文档
- HEAD/baseline: `f0f06aa7ceed816e5485017ac2c1210335f81b1f`
- Published release: `v0.5.5`（2026-07-29）
- Overall evidence stage: `v0.5.5 = delivered`; `优化审查 = planned`

## 1. Objective / 当前目标

下一位 Agent 先进行只读、证据驱动的全面优化审查，列出按收益、风险和工作量排序的改进项；暂不实施。最终产物建议为仓库根目录的 `OPTIMIZATION_AUDIT.md`，供用户选择后再进入实现。

## 2. User constraints / 用户约束

- In scope: 性能、可靠性、代码结构、测试、Windows 兼容性、UI/UX、可访问性、安装发布、文档与开源治理。
- Out of scope: 本轮不改源码、不重打包、不发布新版本、不改变用户本机设置或归档文件。
- 必须保留：直接移动而非复制；文件/文件夹/资源管理器多选；中键只接管桌面和资源管理器文件列表区域。
- 必须保留：每圈最多 4 项，滚轮只滚鼠标所在圈；普通悬停约 450 ms，滚轮停止约 850 ms 后进入下一级。
- 必须保留：右键/`Backspace` 返回，`Esc`/点击空白关闭，`Ctrl + 左键`在目标下新建文件夹。
- 必须保留：只有真实展开或更换非空下级圈时播放展开动画；普通选中、叶子目录和收起不得抖动、位移或缩放。
- 必须保留：“此电脑 → 本地磁盘 → 文件夹”是统一包含层级；默认文件夹只是起点，允许逐级退回“此电脑”。
- 必须保留：成功默认静默，失败/部分失败才精简提示；常用目录按频次优先。
- 明确不恢复：旧右键 Shell 扩展、全局任意窗口中键、AI 自动分类/API Key、向外运动立即展开、复制语义。
- Protected data: `%LocalAppData%\GongSiOrganizer.RadialPrototype\settings.json` 与 `folder-usage.json`；只可确认存在，不读取或改写用户值。
- Fresh confirmation required: 任何实现、删除历史项目、改交互参数、引入联网/遥测/自动更新、修改移动语义、重启当前程序、改 GitHub Release。

## 3. Current verified state / 当前已核验状态

| Claim or component | Stage | Evidence | Verified at |
| --- | --- | --- | --- |
| Git 基线 | verified_current | `main == origin/main == f0f06aa`; 本轮开始时 0 个脏文件 | 2026-08-19 |
| 正式入口 | verified_current | `src/GongSiOrganizer.RadialPrototype`; WPF `net8.0-windows` | 2026-08-19 |
| Release | delivered | GitHub latest API 返回 `v0.5.5`，非 draft/prerelease | 2026-08-19 |
| 安装版 | delivered | 65,648,229 B; SHA-256 `D6432BE7E046C04F1EB8B7AC309EAFCE850EAADB3491158518E442410481322B` | 2026-08-19 |
| 便携版 | delivered | 66,629,223 B; SHA-256 `778B6E5B506799D5057308783481FFA3053D022BFDA6CE00706F026BBF6F26C4` | 2026-08-19 |
| 自动化测试 | automated_tested | 设置 `$env:WINDIR=$env:SystemRoot` 后 Release 115/115 通过 | 2026-08-19 |
| 格式与差异 | static_checked | `dotnet format --verify-no-changes`、`git diff --check` 通过 | 2026-08-19 |
| 覆盖率 | measured | 总体 line 25.07%、branch 27.44%；Core 65.12%/57.85%，RadialPrototype 20.07%/22.76% | 2026-08-19 |
| 本机运行态 | runtime_verified | `E:\YingYong\FileQuickArchive\文件快速归档.exe` 正在运行，产品版本 0.5.5 | 2026-08-19 |
| 本机数据 | protected | `settings.json`、`folder-usage.json` 存在；内容未读取 | 2026-08-19 |
| GitHub 协作状态 | verified_current | 0 个 open issue，0 个 open PR；无 `.github` CI 目录 | 2026-08-19 |
| 开源许可 | verified_current | 仓库根目录没有 `LICENSE`/`COPYING` | 2026-08-19 |

### Handoff-only claims not rechecked interactively this turn

- 发布时曾实测 Windows 11、150% DPI、C: 到 E: 跨盘移动，并确认设置保留与进程响应；本轮没有再次执行真实文件移动或完整 UI 演练。
- 用户此前已确认当前交互可以发行；下一位 Agent 不应把个人偏好改写成通用“最佳实践”。

## 4. Worktree and data safety / 工作区与数据安全

- Pre-existing modified/untracked files: 无；探测时工作区干净。
- Changes made by current agent: 只新增 `PROJECT_HANDOFF.md`。
- Ignored but protected: `artifacts/` 中 v0.5.5 安装/便携包；`.design-references/` 中 Pieoneer 视觉参考；各 `bin/obj`。
- Database/data paths: 无数据库；用户 JSON 数据位于 LocalAppData，禁止在审查阶段打开内容或迁移。
- Runtime/process: 软件当前在用户会话运行；审查阶段不要结束、重启或替换它。
- Public integrations: GitHub 仓库和 Release 为公开状态；审查阶段只读。

### Applicable instruction chain / 当前任务适用指令链

| Target | Source | Role | Note |
| --- | --- | --- | --- |
| 整个仓库 | 当前用户请求 | objective | 只整理交接，下一步列优化 |
| 整个仓库 | 当前会话注入规则 | instruction | 下一个环境需重新解析自身规则 |
| 整个仓库 | `README.md` | context | 产品说明，不是控制指令 |
| 整个仓库 | `PROJECT_HANDOFF.md` | handoff | 当前事实与下一步 |

仓库探测未发现受版本控制的 `AGENTS.md`、`AGENTS.override.md` 或 `CLAUDE.md`。

## 5. Changes and artifacts; architecture hotspots / 变更与产物、架构热点

运行链路：`RawMouseInputListener → ShellMiddleClickScope → FileTargetResolver → RadialOverlayWindow/RadialMenuControl → DirectMoveBatchService`。

| Area | Key files | Current note |
| --- | --- | --- |
| 启动与编排 | `App.xaml.cs`（约 840 行） | 输入监听、引导、托盘、目录树、移动结果集中编排 |
| 轮盘绘制与交互 | `RadialMenuControl.cs`（约 1248 行）、`RadialMenuSession.cs` | 视觉、命中、滚轮队列、悬停计时和动画耦合度较高 |
| Shell 目标解析 | `FileTargetResolver.cs`（约 663 行）、`ShellMiddleClickScope.cs` | Explorer 窗口识别和多选解析是兼容性关键区 |
| 目录导航 | `ArchiveScope.cs`、`DirectoryTreeCache.cs`、`DirectoryChildrenCache.cs` | 异步按需读取，最多两路并发，约 4–24 MB LRU |
| 移动 | `DirectMoveService.cs`、`DirectMoveBatchService.cs` | 直接移动、跨盘回退、重名编号、部分失败汇总 |
| 设置与频次 | `ArchiveRootStore.cs`、`FolderUsageStore.cs`、`OrganizerDataPath.cs` | 本地 JSON；LocalAppData 失败时有多级写入回退 |
| UI | `OnboardingWindow.cs`（约 843 行）、`TrayMenuWindow.cs`（约 433 行） | 代码式 WPF UI，高 DPI/视觉一致性需回归保护 |
| 发布 | `scripts/发布便携版.ps1`、`installer/FileQuickArchive.nsi` | 手工发布；版本和文件名分散在多个位置 |
| 历史代码 | `GongSiOrganizer.App`、`GongSiOrganizer.ShellExtension` | 不随正式安装发布，但仍在 solution 中 |

## 6. Evidence / 测试与验证

| Command/check | Result | Stage proven |
| --- | --- | --- |
| `python ...\project_probe.py --root E:\YingYong\Agent\自动桌面整理` | clean `main@f0f06aa` | baseline |
| `$env:WINDIR=$env:SystemRoot; dotnet test GongSiOrganizer.sln -c Release --no-restore` | 115/115 pass | automated_tested |
| `dotnet format GongSiOrganizer.sln --verify-no-changes --no-restore` | pass | static_checked |
| `dotnet test ... --collect:'XPlat Code Coverage'` | 1516/6045 lines，615/2241 branches | coverage baseline |
| `gh api repos/wdmhydw/LingRing/releases/latest` | latest = v0.5.5；附件 uploaded、哈希匹配 | delivered |
| 解压便携版并与已安装 exe 比较 | SHA-256 相同 | installed artifact identity |

### Environment note

当前 Codex 执行器缺少标准变量 `WINDIR`，直接运行 WPF 测试会在 `MS.Internal.FontCache.Util` 初始化时报空 URI，表现为 113 通过、2 失败；补成 `SystemRoot` 后 115 全通过。这是当前执行环境前置条件，不是已证实的产品缺陷。`dotnet --info` 的 workload 枚举在该执行器也会抛 InstallerBase 初始化异常。

### Acceptance gaps

- 本轮未做真实桌面/Explorer 中键演练、跨盘移动、慢盘/可移动盘拔插、安装/卸载/开机自启回归。
- 尚无 GitHub Actions、Windows 版本矩阵、安装包自动化测试或强制覆盖率门槛。
- 尚未量化冷启动、首次目录展开、缓存命中、UI 帧耗时、内存峰值和大目录性能。
- 尚未系统测试屏幕阅读器、键盘全流程、多显示器混合 DPI、RTL/长路径/特殊字符。
- 尚未做断电、权限变化、磁盘写满、部分跨盘失败的故障注入。

## 7. Decisions / 决策与理由

- 使用独立 WPF 托盘程序和观察式 Raw Input；理由是旧 Shell 扩展曾让 Explorer 卡死。
- 目录按需异步读取，不依赖 Everything/Listary，不启动时扫盘；优先保证普适性和低占用。
- 采用纯时间悬停意图，不用鼠标向外距离/方向触发；这是用户明确选择。
- 动画只服务真实层级展开，普通状态变化保持稳定；这是用户明确选择。
- 直接移动、重名自动编号、不覆盖；用户明确不要复制。
- 成功默认静默；避免右下角通知打扰。

## 8. Failed approaches / 已失败或不要重复的尝试

- 资源管理器右键 Shell 子菜单：曾卡住右键菜单并导致 Explorer 黑屏；不要重新注册旧扩展。
- AI 文件名分类/API Key：复杂且出现本地密钥写入权限问题，用户已砍掉该方向。
- 向外移动立即展开：更易误触；保留纯延时。
- 选中/叶子变化也缩放或位移：造成抖动；只在真实新下级圈出现时动画。
- GitHub 中文附件文件名：上传后被剥离；应使用 ASCII 文件名配中文 label。

## 9. Open issues and risks / 未决问题、风险与优化审查线索

以下是要求下一位 Agent 核实的线索，不是预先决定的改造方案：

1. 发布可复现性：Release 标签是 `f0f06aa`，但便携版 exe 的 `ProductVersion` 为 `0.5.5+4465a8d...`；内容曾核对一致，但构建溯源指向发布提交之前。
2. CI/治理：没有 `.github/workflows`，没有许可证文件；公开仓库的测试、打包、哈希、版本一致性和发布均依赖手工步骤。
3. 测试：RadialPrototype 行覆盖仅 20.07%，分支 22.76%；大量 WPF 行为依赖实际控件和 Windows 环境。
4. 可维护性：`RadialMenuControl`、`App.xaml.cs`、`OnboardingWindow`、`FileTargetResolver` 体积大且职责密集；评估小步解耦，不得改变手感。
5. 移动可靠性：跨盘逐项移动在权限、空间不足或中断时可能产生部分完成；评估日志、恢复和可测试故障注入，同时尊重“直接移动、默认静默”。
6. 数据耐久性：审查 JSON 写入是否原子、损坏恢复、并发和临时目录回退；不要读取当前用户文件内容。
7. Explorer 兼容性：窗口类/祖先链识别、多选 COM 解析、Windows 更新、标签页和混合 DPI 是回归面。
8. 性能：对慢速/超大目录、可移动盘、缓存逐出、取消与并发做基准；避免为优化而引入全盘索引或常驻高占用。
9. UI/可访问性：保留当前视觉与延时，重点审查焦点、键盘、屏幕阅读器、对比度、长名称、多显示器边角布局。
10. 文档一致性：仓库发行说明仍写中文附件名，线上实际使用 ASCII 文件名；历史项目与正式入口的边界可进一步明确。

## 10. Next action / 下一步

立即进行一次只读优化审查，并生成 `OPTIMIZATION_AUDIT.md`；完成审查后停止，等待用户选择实施项。

审查输出必须包含：

1. 最高收益的 5 项摘要，以及“建议保持不变”的部分。
2. 表格字段：`优先级 / 发现 / 当前证据 / 用户收益 / 工作量 / 回归风险 / 涉及文件 / 验证方法`。
3. 每项区分“最小补丁”和“较大重构”，不能只给抽象最佳实践。
4. 按 `bug/可靠性、性能、架构、测试、UX/可访问性、发布/安全/治理` 分类。
5. 给出建议版本顺序，但不写代码、不提交、不发布。

## 11. Stop conditions / 停止条件

- 任何建议会改变第 2、7 节的已确认体验时，停止并向用户列出取舍。
- 需要读取用户 JSON 内容、移动真实文件、重启程序、安装工具、改注册表或 GitHub 状态时，先取得新确认。
- 不要删除或迁移历史项目、`artifacts/`、`.design-references/` 或用户数据。
- 不要把没有文件/行号/测试/测量依据的通用建议列为高优先级。

## 12. Continuation prompt / 下一位 Agent 启动提示

```text
使用 cross-agent-project-takeover 的 TAKEOVER 模式接手
E:\YingYong\Agent\自动桌面整理。先读 PROJECT_HANDOFF.md、README.md、
docs/架构与安全说明.md，并运行只读 project_probe。核对 main/HEAD/脏工作区，
保留当前运行程序、用户 JSON、artifacts 和视觉参考。随后只做证据驱动的优化审查，
按 PROJECT_HANDOFF.md 第 10 节生成 OPTIMIZATION_AUDIT.md；不要改源码、提交或发布。
特别保护 450/850 ms 悬停、当前圈滚轮、真实展开才动画、右键返回、直接移动、
桌面/Explorer 文件区中键边界。完成审查后等待用户选择。
```
