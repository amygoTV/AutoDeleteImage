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
- **自动更新**：启动后每天检查一次 GitHub 最新 Release，发现新版本可一键下载并重启安装，保留任务配置和日志。
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

## 自动更新

- 程序启动后每天最多检查一次最新版本，也可以从托盘菜单手动点“检查更新”。
- 发现新版本后提示用户，确认后下载 `AutoDeleteImage-win.zip`，退出后由临时更新脚本替换 `AutoDeleteImage.exe`、`AutoDeleteImage.exe.config` 和 `Delete_image.ico`。
- 更新不会覆盖 `config.ini` 和 `Log`。
- 用户取消更新后，同一版本不再自动提示；托盘菜单仍可手动检查。
- 默认更新源为 GitHub 最新 Release：

```
https://github.com/amygoTV/AutoDeleteImage/releases/latest/download/version.txt
https://github.com/amygoTV/AutoDeleteImage/releases/latest/download/AutoDeleteImage-win.zip
```

如果使用环境无法访问 GitHub，可以在 `config.ini` 中指定内部共享目录或内部 HTTP 服务器：

```ini
[Update]
BaseUrl=D:\Tools\AutoDeleteImage\Update\
```

> v1.3.1 及更早版本没有更新器，需要手动安装一次 v1.4.0，之后才能自动更新。

注意事项：

- 程序需要放在当前用户有写权限的目录（例如 `D:\Tools\AutoDeleteImage`），不要放在 `C:\Program Files`，否则更新脚本无法替换 exe。
- 使用 GitHub 更新源时，客户端电脑必须能访问 `github.com`。
- 更新包只替换程序文件，不会覆盖 `config.ini` 和 `Log`。
- 更新脚本会自动备份旧版本；如果新版本被杀毒软件拦截或无法启动，会自动回滚到旧版本。部分公司电脑的 Microsoft Defender ASR 规则可能会拦截未签名的新程序，此时需要联系 IT 添加信任后重试。

## 发布新版本

仓库已配置 GitHub Actions（`.github/workflows/release.yml`）。发布步骤：

1. 修改 `MainForm.cs` 中的 `APP_VERSION` 和 `开发日志.md`。
2. 提交并推送代码。
3. 打标签并推送：

```bash
git tag v1.4.0
git push origin main --tags
```

4. GitHub Actions 会自动构建、生成 `AutoDeleteImage-win.zip` 和 `version.txt`，并创建 Release。

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
├── UpdateChecker.cs            # 检查更新、下载并启动更新脚本
├── Delete_image.ico            # 程序图标
├── .github/workflows/release.yml # GitHub Actions 自动发布
├── 开发日志.md                 # 版本变更记录
└── README.md                   # 本文件
```

## 版本

当前版本：**v1.4.2**

详细变更见 [开发日志.md](开发日志.md)。
