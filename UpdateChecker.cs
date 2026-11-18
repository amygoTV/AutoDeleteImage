using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;

namespace AutoDeleteImage
{
    /// <summary>
    /// 新版本信息
    /// </summary>
    public class UpdateInfo
    {
        public Version Version { get; set; }
        public string VersionText { get; set; }
        public string DownloadUrl { get; set; }
    }

    /// <summary>
    /// 检查更新结果
    /// </summary>
    public class UpdateCheckResult
    {
        public UpdateInfo Update { get; set; }
        public string Error { get; set; }

        public bool HasUpdate
        {
            get { return Update != null; }
        }
    }

    /// <summary>
    /// 已经下载并解压好的更新包
    /// </summary>
    public class PreparedUpdate
    {
        public string RootDirectory { get; set; }
        public string SourceDirectory { get; set; }
        public string ScriptPath { get; set; }
    }

    /// <summary>
    /// 自动更新：从 GitHub Releases（或内部服务器）读取 version.txt，下载更新包，
    /// 退出后由临时 cmd 脚本替换程序文件并重新启动。
    /// </summary>
    public static class UpdateChecker
    {
        private const string DefaultBaseUrl =
            "https://github.com/amygoTV/AutoDeleteImage/releases/latest/download/";

        private const string VersionFileName = "version.txt";
        private const string PackageFileName = "AutoDeleteImage-win.zip";

        /// <summary>
        /// 更新源地址。默认使用 GitHub 最新 Release；
        /// 可通过 config.ini 的 [Update] BaseUrl 改成内部共享目录或内部 HTTP 服务器。
        /// </summary>
        public static string GetBaseUrl()
        {
            string url = IniHelper.GetValue("Update", "BaseUrl", DefaultBaseUrl);
            if (string.IsNullOrWhiteSpace(url)) url = DefaultBaseUrl;

            url = url.Trim();

            // 允许直接填写本地/共享文件夹路径
            if (Directory.Exists(url))
            {
                try
                {
                    url = new Uri(url.TrimEnd('\\') + "\\").AbsoluteUri;
                }
                catch { }
            }

            if (!url.EndsWith("/")) url += "/";
            return url;
        }

        /// <summary>
        /// 检查最新版本。返回结果中 Update 为 null 且 Error 为空表示已是最新版本。
        /// </summary>
        public static UpdateCheckResult Check(string currentVersion, string baseUrl)
        {
            var result = new UpdateCheckResult();

            try
            {
                Version current;
                if (!Version.TryParse((currentVersion ?? "").Trim(), out current))
                {
                    result.Error = "当前版本号格式不正确";
                    return result;
                }

                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

                string text;
                using (var client = CreateClient(currentVersion))
                {
                    text = client.DownloadString(baseUrl + VersionFileName);
                }

                text = (text ?? "").Trim().TrimStart('v', 'V');

                Version latest;
                if (!Version.TryParse(text, out latest))
                {
                    result.Error = "远程版本文件格式不正确：" + text;
                    return result;
                }

                if (latest <= current) return result;

                result.Update = new UpdateInfo
                {
                    Version = latest,
                    VersionText = text,
                    DownloadUrl = baseUrl + PackageFileName
                };
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
            }

            return result;
        }

        /// <summary>
        /// 下载更新包并解压到临时目录，同时生成替换文件用的 update.cmd。
        /// </summary>
        public static bool DownloadAndPrepare(UpdateInfo info, out PreparedUpdate prepared, out string error)
        {
            prepared = null;
            error = "";

            string root = Path.Combine(Path.GetTempPath(), "AutoDeleteImage_update_" + info.VersionText);

            try
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);

                string sourceDir = Path.Combine(root, "new");
                Directory.CreateDirectory(sourceDir);

                string zipPath = Path.Combine(root, PackageFileName);
                using (var client = CreateClient(info.VersionText))
                {
                    client.DownloadFile(info.DownloadUrl, zipPath);
                }

                ZipFile.ExtractToDirectory(zipPath, sourceDir);

                if (!File.Exists(Path.Combine(sourceDir, "AutoDeleteImage.exe")))
                {
                    error = "更新包中缺少 AutoDeleteImage.exe";
                    TryDeleteDirectory(root);
                    return false;
                }

                string scriptPath = Path.Combine(root, "update.cmd");
                File.WriteAllText(scriptPath, BuildUpdateScript(), Encoding.Default);

                prepared = new PreparedUpdate
                {
                    RootDirectory = root,
                    SourceDirectory = sourceDir,
                    ScriptPath = scriptPath
                };
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                TryDeleteDirectory(root);
                return false;
            }
        }

        /// <summary>
        /// 启动更新脚本。脚本会等待当前进程退出，然后替换文件并重新启动程序。
        /// </summary>
        public static bool StartUpdater(PreparedUpdate update, out string error)
        {
            error = "";

            try
            {
                string appDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
                int pid = Process.GetCurrentProcess().Id;

                // cmd /c ""脚本" PID "程序目录" "新文件目录""
                string arguments = "/c \"\"" + update.ScriptPath + "\" "
                    + pid + " \"" + appDir + "\" \""
                    + update.SourceDirectory + "\"\"";

                var startInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = update.RootDirectory
                };

                Process.Start(startInfo);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// 清理取消更新后留下的临时文件
        /// </summary>
        public static void Cleanup(PreparedUpdate update)
        {
            if (update == null) return;
            TryDeleteDirectory(update.RootDirectory);
        }

        /// <summary>
        /// 清理 7 天前的更新临时目录（下载失败或更新后残留）
        /// </summary>
        public static void CleanupOldUpdates()
        {
            try
            {
                string temp = Path.GetTempPath();
                foreach (var dir in Directory.GetDirectories(temp, "AutoDeleteImage_update_*"))
                {
                    try
                    {
                        if (Directory.GetLastWriteTime(dir) < DateTime.Now.AddDays(-7))
                        {
                            Directory.Delete(dir, true);
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        private static WebClient CreateClient(string version)
        {
            var client = new WebClient();
            client.Headers[HttpRequestHeader.UserAgent] = "AutoDeleteImage/" + (version ?? "1.0");
            return client;
        }

        private static string BuildUpdateScript()
        {
            return "@echo off\r\n"
                + "setlocal\r\n"
                + "set \"PID=%~1\"\r\n"
                + "set \"APP=%~2\"\r\n"
                + "set \"SRC=%~3\"\r\n"
                + ":wait\r\n"
                + "tasklist /FI \"PID eq %PID%\" /NH 2>nul | findstr /R /C:\"[ ]%PID%[ ]\" >nul\r\n"
                + "if not errorlevel 1 (\r\n"
                + "    ping 127.0.0.1 -n 2 >nul\r\n"
                + "    goto wait\r\n"
                + ")\r\n"
                + "if exist \"%APP%\\AutoDeleteImage.exe\" copy /y \"%APP%\\AutoDeleteImage.exe\" \"%APP%\\AutoDeleteImage.exe.bak\" >nul\r\n"
                + "copy /y \"%SRC%\\AutoDeleteImage.exe\" \"%APP%\\AutoDeleteImage.exe\" >nul\r\n"
                + "if errorlevel 1 goto rollback\r\n"
                + "for %%F in (\"%SRC%\\*\") do (\r\n"
                + "    if /I not \"%%~nxF\"==\"AutoDeleteImage.exe\" if /I not \"%%~nxF\"==\"config.ini\" copy /y \"%%F\" \"%APP%\" >nul\r\n"
                + ")\r\n"
                + "start \"\" \"%APP%\\AutoDeleteImage.exe\"\r\n"
                + "ping 127.0.0.1 -n 6 >nul\r\n"
                + "tasklist /FI \"IMAGENAME eq AutoDeleteImage.exe\" /NH 2>nul | findstr /I \"AutoDeleteImage.exe\" >nul\r\n"
                + "if not errorlevel 1 (\r\n"
                + "    del \"%APP%\\AutoDeleteImage.exe.bak\" >nul 2>nul\r\n"
                + "    exit /b 0\r\n"
                + ")\r\n"
                + ":rollback\r\n"
                + "if exist \"%APP%\\AutoDeleteImage.exe.bak\" (\r\n"
                + "    copy /y \"%APP%\\AutoDeleteImage.exe.bak\" \"%APP%\\AutoDeleteImage.exe\" >nul\r\n"
                + "    del \"%APP%\\AutoDeleteImage.exe.bak\" >nul 2>nul\r\n"
                + ")\r\n"
                + "start \"\" \"%APP%\\AutoDeleteImage.exe\"\r\n"
                + "exit /b 1\r\n";
        }

        private static void TryDeleteDirectory(string dir)
        {
            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
            catch { }
        }
    }
}
