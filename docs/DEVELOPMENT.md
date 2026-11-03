# 开发与构建

[返回项目首页](../README.md) · [文档索引](README.md)

## 环境

- Windows x64；正式程序使用 WPF、Win32 输入与资源管理器 COM 集成。
- .NET SDK **8.0.422**，由 [global.json](../global.json) 固定；依赖通过各项目的 `packages.lock.json` 锁定。
- 制作 Windows 安装程序时使用 [NSIS](https://nsis.sourceforge.io/)，现有脚本默认位置为 `C:\Program Files (x86)\NSIS\makensis.exe`。

当前正式运行入口为 `src/GongSiOrganizer.RadialPrototype`，主要业务规则位于 `src/GongSiOrganizer.Core`。详见 [架构说明](架构与安全说明.md)。

## 还原与测试

在仓库根目录运行 PowerShell：

```powershell
$env:WINDIR = $env:SystemRoot
dotnet restore GongSiOrganizer.sln -r win-x64 --locked-mode
dotnet test tests\GongSiOrganizer.Core.Tests\GongSiOrganizer.Core.Tests.csproj -c Release --no-restore
```

锁定还原失败时，先核对 SDK、运行时目标与锁文件；发行构建沿用已提交的依赖版本。

## 隔离预览

以下入口只运行安全演示，不注册全局中键监听、不移动真实文件。显示设置使用单独的临时目录，避免读取当前用户的设置。

```powershell
$priorDataRoot = $env:LINGRING_DATA_ROOT
try {
    $env:LINGRING_DATA_ROOT = Join-Path $env:TEMP 'LingRing-DeveloperPreview'
    dotnet run --project src\GongSiOrganizer.RadialPrototype -c Release --no-restore -- --demo --no-listener
}
finally {
    $env:LINGRING_DATA_ROOT = $priorDataRoot
}
```

托盘中的“退出灵环·文件快速整理”可结束演示进程。更多预览参数见 [使用说明](同心圆原型使用说明.txt)。

## 打包入口

先提交源码并确认工作区干净。现有脚本从固定提交创建临时工作树，还原锁定依赖、运行测试，再生成同次构建的安装版与便携版。

```powershell
# 仅在确实需要试用包时执行。
.\scripts\Build-LocalTrial.ps1

# 已获得正式发布授权时使用 release 通道。
.\scripts\Build-LocalTrial.ps1 -Channel release
```

版本来源为主程序的 `.csproj`；安装器脚本是 [LingRing.nsi](../installer/LingRing.nsi)。构建目录自动移除，正式归档保留于 `Releases/LingRing/<版本>/windows-x64/`。相同版本的归档已存在时，构建脚本会停止，保护已有发行物。

新构建归档在生成公开附件清单前，使用 `scripts/Verify-LocalTrial.ps1` 并通过 `-ArchivePath` 指定该归档，检查安装载荷与原生预览。已发布版本的结果见 [0.7.0 验收记录](RELEASE_VERIFICATION_0.7.0.md)。

上传与下载回读完成后，依照 [暂存清理流程](RELEASE_CLEANUP.md) 收尾。文档修改本身无需重新生成安装包。

## 反馈与贡献

通过 [Issues](https://github.com/wdmhydw/LingRing/issues) 提交问题或建议，描述版本、重现步骤、实际与预期结果。提交界面变更时提供合成目录截图；涉及移动逻辑时说明测试覆盖与数据边界。
