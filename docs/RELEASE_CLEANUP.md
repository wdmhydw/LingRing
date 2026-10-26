# 发布后的临时目录清理

用户于 2026-10-26 要求清理本次构建和上传临时目录、保留正式发布包，并要求后续发布正常清理。该要求持续适用于本项目的发布收尾。历史版本、源码工作树、截图证据和用户数据仍各自保留。

## 固定收尾步骤

1. 干净构建工作树由 `Build-LocalTrial.ps1` 的 `finally` 释放；运行验证工作区由 `VerificationWorkspace.ps1` 释放。
2. 发布前将正式包、完整内部索引、测试记录和独有的验证证据归档到 `Releases/LingRing/<version>/windows-x64/`。
3. GitHub 与私人备份发布完成后，将两端实际下载回读的 `github-readback.json`、`private-readback.json` 及 `publication-verification.json` 存入归档，并更新内部 `archive-manifest.json`。下载记录必须包含对应版本的四个附件、大小及 SHA-256；发布记录标记 `RELEASE_READY` 且两端发布、远端回读均成功。
4. 执行固定清理入口；此步骤为发布收尾的必做项，见根目录 `release.manifest.yaml` 的 `cleanup`：

   ```powershell
   # 预览只读，不产生文件、不清理目录。
   pwsh ./scripts/Clear-ReleaseStaging.ps1 -Version 0.6.4 -WhatIf
   # 完成归档和远端回读后执行。
   pwsh ./scripts/Clear-ReleaseStaging.ps1 -Version 0.6.4
   ```

5. 核实本版本的 `artifacts/publish-<version>` 和 `artifacts/dev-<version>` 均已离开项目目录，清理记录与内部归档索引一致。正式包、公开清单、校验文本及已发布 tag 保持原值。

入口仅接受版本号，目标固定为对应版本的两个目录；先核对完整归档、来源提交、两端下载校验值和发布状态，再检查全部目标及父目录，遇到链接、Git 工作树或不一致时停止。不会扫描删除其他版本或任意命名的目录。

清理采用 Windows 回收站，保留恢复能力。`cleanup-verification.json` 记录路径、文件数、字节数、结果和时间；回收站占用不计作释放的磁盘空间，也不自动清空整个回收站。若中途失败，记录 `incomplete` 和剩余目标，发布收尾继续作为未完成事项。重复执行会重新验证归档，保留既有清理记录。

## 本次拦截排查

2026-10-26 的递归永久删除在 PowerShell 进程创建前返回 `blocked by policy`。当前会话记录为 `Never` / `DangerFullAccess`；本地用户规则中只有已有的允许规则，官方 `codex execpolicy check` 对原命令返回 `matchedRules: []`。会话记录及桌面日志未给出规则编号或进一步理由。因此已确认执行工具拦截了该操作，具体判定原因未披露；不将其归因为 Windows 文件权限、占用或某条已知本地禁令。

按 [Codex 官方规则文档](https://learn.chatgpt.com/docs/agent-configuration/rules) 检查本地规则只覆盖所提供的规则文件，并不等同于获得其他执行层的判定解释。本次可恢复的回收站清理成功，两个目录的回收站原路径已核对。

## 验证

```powershell
pwsh ./tests/Verify-ReleaseStagingCleanup.ps1
```

回归覆盖只读预览、损坏归档、远端哈希不一致、未完成发布、路径越界、Git 工作树保护、根/子级/父级链接保护、正式包和其他版本保留，以及重复清理。样本使用隔离临时仓库；真实发布包不参与写入测试。
