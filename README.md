<p align="center">
  <img src="src/GongSiOrganizer.RadialPrototype/Assets/GongSiOrganizer.png" width="112" alt="文件快速归档图标">
</p>

<h1 align="center">文件快速归档</h1>

<p align="center">在桌面或文件资源管理器中，用鼠标中键呼出同心圆目录轮盘，把文件和文件夹快速移动到归档目录。</p>

## 下载

[前往 Releases 下载最新版 Windows 安装版或便携版](https://github.com/wdmhydw/FileQuickArchive/releases/latest)

当前版本：`v0.5.3`。发行包面向 Windows x64，并已在 Windows 11 上完成实际桌面验证。

## 核心功能

- 在桌面或文件资源管理器的文件区域单击中键，原地打开目录轮盘。
- 支持文件、文件夹以及资源管理器多选项目的批量移动。
- 鼠标悬停展开下一级目录；每层显示 4 个目录，滚轮循环切换同级目录。
- 右键或 `Backspace` 返回上一级，`Esc` 或点击空白处关闭。
- 在目标目录上按 `Ctrl + 左键`，可以即时新建子文件夹并命名。
- 按使用频次自动调整同级目录顺序，常用目标优先出现。
- 归档根目录可以在新手引导或托盘菜单中随时更改。
- 高 DPI 界面、托盘菜单、开机自启选项和中文新手引导。

## 使用方法

1. 安装并启动“文件快速归档”。
2. 在新手引导最后一步选择目标归档文件夹。
3. 在桌面或文件资源管理器文件区域的文件、文件夹上单击鼠标中键。
4. 移入扇区逐级选择目标目录，左键单击扇区或中心圆确认移动。

| 操作 | 功能 |
| --- | --- |
| 鼠标中键 | 在鼠标位置打开轮盘 |
| 鼠标悬停 | 展开对应目录的下一级 |
| 滚轮 | 切换鼠标所在层级的同级目录 |
| 鼠标左键 | 确认移动到当前目标 |
| 鼠标右键 / `Backspace` | 返回上一级 |
| `Ctrl + 鼠标左键` | 在悬停目录中新建子文件夹 |
| `Esc` / 点击空白处 | 关闭轮盘 |

## 安全边界

- 只在 Windows 桌面和文件资源管理器文件列表区域响应中键；浏览器、聊天软件、任务栏、资源管理器工具栏和其他软件的文件选择框不会触发。
- 归档动作是直接移动，不会复制一份副本，也不会覆盖同名项目；重名时自动生成可用名称。
- 目标始终限制在当前归档根目录及其子目录中。
- 禁止把文件夹移动到自身或其子目录。
- 不联网、不读取 API Key，不上传文件名或目录结构。
- 不再安装曾导致资源管理器卡顿的右键 Shell 扩展。

> 左键确认后不会出现第二次确认，也不会加入资源管理器撤销队列。首次使用建议先用测试文件熟悉操作。

## 兼容性提示

- 当前安装版和便携版均为 Windows x64 自包含版本，无需用户另装 .NET Runtime。
- 如果正在使用 Listary，请在 Listary 的“选项 → 集成”中关闭 Desktop 和 File Explorer 的鼠标中键菜单，避免两个程序同时响应。

## 从源码构建

需要 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)。

```powershell
dotnet restore GongSiOrganizer.sln
dotnet test GongSiOrganizer.sln -c Release
dotnet publish src\GongSiOrganizer.RadialPrototype\GongSiOrganizer.RadialPrototype.csproj `
  -c Release -r win-x64 --self-contained true
```

安装程序使用 [NSIS](https://nsis.sourceforge.io/) 构建，脚本位于 `installer/FileQuickArchive.nsi`。

## 文档

- [完整使用说明](docs/同心圆原型使用说明.txt)
- [架构与安全说明](docs/架构与安全说明.md)
- [v0.5.3 发行说明](docs/release-v0.5.3.md)
