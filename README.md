# AutoDeleteImage 自动删图工具

一个基于 **.NET Framework 4.8 + WinForms** 的 Windows 桌面小工具，用于按保留天数自动清理图片、CSV、TXT 等文件，适合视觉检测产线的图片/日志目录维护。

## 功能

- **多任务管理**：最多支持 20 个删除任务，每个任务可单独启用/禁用。
- **按保留天数清理**：根据文件最后修改时间判断是否过期。
- **自定义文件格式**：每个任务可指定要清理的扩展名。
- **多种触发方式**：
  - 手动执行 / 定时监控
  - 电脑空闲指定分钟数后执行
  - 磁盘剩余空间低于阈值时执行
  - 每天固定时间执行
- **递归清理子文件夹**：文件删空后的子文件夹会一并删除，任务根目录始终保留。
- **详细运行日志**：按天保存到 `Log\yyyy-MM-dd.log`，记录任务开始、每个删除的文件、删除的空文件夹、跳过的子文件夹及汇总结果。
- **后台运行**：关闭窗口最小化到托盘，支持开机自启。
- **状态栏提示**：未运行白底、运行中绿底、失败红底。

## 递归清理安全规则

为避免误删，只有同时满足以下条件的子文件夹才会被递归清理：

1. 该子文件夹及其**所有下级**只能包含图片、CSV、TXT 文件。
2. 只要出现任何其他格式（如 pdf、exe、doc、json 等），**整个子文件夹跳过，不删除任何内容**。
3. 任务根目录下直接存放的文件仍按任务规则清理，根目录本身永远保留。
4. 符号链接/联接点自动跳过，不会越界删除。
5. 文件被占用或无权限时自动跳过，不影响其他文件。

允许递归的格式：

```
jpg jpeg jpe jfif png bmp gif tif tiff webp heic heif csv txt
```

> 注意：删除是永久删除，不进入回收站，请谨慎配置任务目录。

## 配置文件与日志

| 文件 | 位置 | 说明 |
|---|---|---|
| `config.ini` | 程序启动目录（exe 同级） | 保存任务、触发条件等配置 |
| `Log\yyyy-MM-dd.log` | 程序启动目录 | 按天保存运行日志，UTF-8 编码 |

日志示例：

```
[2026-11-18 11:04:53] [TestTask] 开始手动清理，目录：D:\Images
[2026-11-18 11:04:53] 删除文件：D:\Images\2026-10-01\a.jpg
[2026-11-18 11:04:53] 删除空文件夹：D:\Images\2026-10-01
[2026-11-18 11:04:53] [TestTask] 手动执行完成，删除 1 个文件，清理 1 个空文件夹
```

## 技术栈

| 项 | 说明 |
|---|---|
| 框架 | .NET Framework 4.8 |
| 界面 | Windows Forms |
| 项目类型 | SDK-style Windows Desktop 项目 |
| 外部依赖 | 无 NuGet 包，仅引用 `System.Management` |
| 语言版本 | C# 7.3 |

## 构建与运行

需要安装 .NET SDK 和 .NET Framework 4.8 Developer Pack。

```bash
dotnet build AutoDeleteImage.csproj
```

编译输出位于 `bin\Debug\net48\AutoDeleteImage.exe`，双击运行即可。

## 项目结构

```
AutoDeleteImage/
├── AutoDeleteImage.csproj      # 项目文件
├── Program.cs                  # 程序入口
├── MainForm.cs                 # 主窗体 UI 与任务管理
├── AutoCleanService.cs         # 后台清理服务、递归规则
├── TaskItem.cs                 # 任务配置模型
├── IniHelper.cs                # config.ini 读写
├── IdleDetector.cs             # 电脑空闲检测
├── AutoStartHelper.cs          # 开机自启
├── LogHelper.cs                # 按天文件日志
├── Delete_image.ico            # 程序图标
├── 开发日志.md                 # 版本变更记录
└── README.md                   # 本文件
```

## 版本

当前版本：**v1.3.1**

详细变更见 [开发日志.md](开发日志.md)。
