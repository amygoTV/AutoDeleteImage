using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AutoDeleteImage
{
    /// <summary>
    /// 后台文件清理服务：负责自动触发监控和手动立即执行。
    /// 所有磁盘操作都在后台线程完成，并主动降速，避免占用过多 CPU / 磁盘。
    /// </summary>
    public class AutoCleanService
    {
        private readonly object _sync = new object();

        private CancellationTokenSource _monitorCts;
        private Task _monitorTask;
        private CancellationTokenSource _runCts;
        private Task _runTask;

        // 0 = 空闲，1 = 正在删除文件（手动和自动共用，避免同时扫盘）
        private int _busy = 0;

        // 递归清理子文件夹时允许的格式：图片 + CSV + TXT
        private static readonly HashSet<string> RecursiveSafeExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "jpg", "jpeg", "jpe", "jfif", "png", "bmp", "gif",
            "tif", "tiff", "webp", "heic", "heif", "csv", "txt"
        };

        public event Action<string> LogMessage;
        public event Action RunStarted;
        public event Action<int> TaskCompleted;   // 参数：本轮删除的文件数
        public event Action TasksChanged;         // 任务统计有变化，界面需要刷新

        public bool IsRunning
        {
            get { return Volatile.Read(ref _busy) == 1; }
        }

        public bool IsMonitoring
        {
            get
            {
                var task = _monitorTask;
                return task != null && !task.IsCompleted;
            }
        }

        /// <summary>
        /// 启动后台监控（幂等：重复调用不会产生多个监控线程）
        /// </summary>
        public void Start(List<TaskItem> tasks)
        {
            lock (_sync)
            {
                if (_monitorTask != null && !_monitorTask.IsCompleted) return;

                try { if (_monitorCts != null) _monitorCts.Dispose(); } catch { }

                _monitorCts = new CancellationTokenSource();
                var token = _monitorCts.Token;
                _monitorTask = Task.Run(() => MonitorLoop(tasks, token));
            }
        }

        /// <summary>
        /// 立即执行一次所有启用的任务（手动按钮）
        /// 返回 false 表示当前正有清理任务在执行
        /// </summary>
        public bool RunNow(List<TaskItem> tasks)
        {
            lock (_sync)
            {
                if (!TryBeginRun()) return false;

                try { if (_runCts != null) _runCts.Dispose(); } catch { }

                _runCts = new CancellationTokenSource();
                var token = _runCts.Token;

                try
                {
                    _runTask = Task.Run(() => ExecuteTasksImmediate(tasks, token));
                }
                catch
                {
                    EndRun(0);
                    throw;
                }

                return true;
            }
        }

        /// <summary>
        /// 停止监控并取消正在执行的清理
        /// </summary>
        public void Stop()
        {
            CancellationTokenSource monitorCts;
            CancellationTokenSource runCts;
            Task monitorTask;
            Task runTask;

            lock (_sync)
            {
                monitorCts = _monitorCts;
                runCts = _runCts;
                monitorTask = _monitorTask;
                runTask = _runTask;
            }

            try { if (monitorCts != null) monitorCts.Cancel(); } catch { }
            try { if (runCts != null) runCts.Cancel(); } catch { }

            var all = new List<Task>();
            if (monitorTask != null) all.Add(monitorTask);
            if (runTask != null) all.Add(runTask);

            if (all.Count > 0)
            {
                try { Task.WaitAll(all.ToArray(), 2000); } catch { }
            }

            Volatile.Write(ref _busy, 0);
        }

        public void Dispose()
        {
            Stop();

            lock (_sync)
            {
                try { if (_monitorCts != null) _monitorCts.Dispose(); } catch { }
                try { if (_runCts != null) _runCts.Dispose(); } catch { }
            }
        }

        // ==================== 自动监控 ====================

        private async Task MonitorLoop(List<TaskItem> tasks, CancellationToken token)
        {
            try
            {
                // 启动后先等 5 秒，避免影响程序启动
                await Task.Delay(5000, token);

                while (!token.IsCancellationRequested)
                {
                    TaskItem[] snapshot;
                    try
                    {
                        snapshot = tasks.ToArray();
                    }
                    catch (InvalidOperationException)
                    {
                        // 界面正在修改任务列表，下一轮再处理
                        snapshot = new TaskItem[0];
                    }

                    foreach (var task in snapshot)
                    {
                        if (token.IsCancellationRequested) break;
                        if (task == null || !task.Enabled) continue;
                        if (!ShouldAutoRun(task)) continue;
                        if (!TryBeginRun()) break;

                        int deleted = 0;
                        int removedFolders = 0;
                        int skippedFolders = 0;
                        try
                        {
                            RaiseLog($"[{task.Name}] 开始自动清理，目录：{task.FolderPath}");
                            deleted = ProcessSingleTask(task, token, out removedFolders, out skippedFolders);
                            task.DeletedCount += deleted;
                            task.LastRunTime = DateTime.Now;
                            task.LastError = "";

                            RaiseLog($"[{task.Name}] 自动清理完成，删除 {deleted} 个文件{FormatCleanExtra(removedFolders, skippedFolders)}");
                            RaiseTasksChanged();
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                        catch (Exception ex)
                        {
                            task.LastError = ex.Message;
                            RaiseLog($"[{task.Name}] 出错：{ex.Message}");
                        }
                        finally
                        {
                            EndRun(deleted);
                        }

                        // 任务之间歇 2 秒，降低连续扫盘的压力
                        await Task.Delay(2000, token);
                    }

                    // 每 30 秒检查一次触发条件
                    await Task.Delay(TimeSpan.FromSeconds(30), token);
                }
            }
            catch (OperationCanceledException)
            {
                // 正常停止，不提示
            }
            catch (Exception ex)
            {
                RaiseLog("监控异常：" + ex.Message);
            }
        }

        private bool ShouldAutoRun(TaskItem task)
        {
            DateTime now = DateTime.Now;

            try
            {
                switch (task.TriggerMode)
                {
                    case 1: // 电脑空闲
                        if (!IdleDetector.IsIdleForMinutes(task.IdleMinutes)) return false;
                        return HasCooldownPassed(task, now, TimeSpan.FromMinutes(60));

                    case 2: // 磁盘空余不足
                        if (!IsDiskSpaceLow(task.TargetDriveLetter, task.DiskFreeThresholdGB)) return false;
                        return HasCooldownPassed(task, now, TimeSpan.FromMinutes(30));

                    case 3: // 每日定时
                        if (task.LastRunTime.Date == now.Date) return false;
                        return IsScheduledTime(task.ScheduleTime);

                    default: // 0 = 手动/定时检查，不自动执行
                        return false;
                }
            }
            catch
            {
                return false;
            }
        }

        private static bool HasCooldownPassed(TaskItem task, DateTime now, TimeSpan cooldown)
        {
            if (task.LastRunTime == DateTime.MinValue) return true;
            return now - task.LastRunTime >= cooldown;
        }

        private static bool IsDiskSpaceLow(string driveLetter, double thresholdGB)
        {
            try
            {
                string drive = string.IsNullOrWhiteSpace(driveLetter)
                    ? "C:"
                    : driveLetter.Trim().Substring(0, 1).ToUpperInvariant() + ":";

                var driveInfo = new DriveInfo(drive);
                if (!driveInfo.IsReady) return false;

                double freeGB = driveInfo.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
                return freeGB < thresholdGB;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsScheduledTime(string timeStr)
        {
            try
            {
                var parts = timeStr.Split(':');
                int hour = int.Parse(parts[0]);
                int minute = int.Parse(parts[1]);

                var now = DateTime.Now;
                var target = new DateTime(now.Year, now.Month, now.Day, hour, minute, 0);

                // 允许 60 秒误差，避免 30 秒轮询刚好错过时间点
                return Math.Abs((now - target).TotalSeconds) <= 60;
            }
            catch
            {
                return false;
            }
        }

        // ==================== 手动执行 ====================

        private async Task ExecuteTasksImmediate(List<TaskItem> tasks, CancellationToken token)
        {
            int totalDeleted = 0;

            try
            {
                var enabledTasks = tasks.Where(t => t != null && t.Enabled).ToList();

                foreach (var task in enabledTasks)
                {
                    if (token.IsCancellationRequested) break;

                    try
                    {
                        RaiseLog($"[{task.Name}] 开始手动清理，目录：{task.FolderPath}");

                        int removedFolders;
                        int skippedFolders;
                        int deleted = ProcessSingleTask(task, token, out removedFolders, out skippedFolders);
                        task.DeletedCount += deleted;
                        task.LastRunTime = DateTime.Now;
                        task.LastError = "";
                        totalDeleted += deleted;

                        RaiseLog($"[{task.Name}] 手动执行完成，删除 {deleted} 个文件{FormatCleanExtra(removedFolders, skippedFolders)}");
                        RaiseTasksChanged();
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        task.LastError = ex.Message;
                        RaiseLog($"[{task.Name}] 出错：{ex.Message}");
                    }

                    await Task.Delay(500, token);
                }
            }
            catch (OperationCanceledException)
            {
                RaiseLog("清理任务已停止");
            }
            catch (Exception ex)
            {
                RaiseLog("致命错误：" + ex.Message);
            }
            finally
            {
                EndRun(totalDeleted);
            }
        }

        // ==================== 实际删除 ====================

        private int ProcessSingleTask(TaskItem task, CancellationToken token, out int removedFolders, out int skippedFolders)
        {
            removedFolders = 0;
            skippedFolders = 0;

            if (string.IsNullOrWhiteSpace(task.FolderPath)) return 0;

            string root;
            try
            {
                root = Path.GetFullPath(task.FolderPath);
            }
            catch
            {
                return 0;
            }

            if (!Directory.Exists(root)) return 0;

            var extensions = new HashSet<string>(
                ParseExtensions(task.FileExtensions),
                StringComparer.OrdinalIgnoreCase);

            if (extensions.Count == 0) return 0;

            int retentionDays = Math.Max(0, task.RetentionDays);
            DateTime cutoff = DateTime.Now.AddDays(-retentionDays);
            int deleted = 0;
            int processed = 0;

            var dirsWithDeletedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1) 任务根目录下的文件：按原规则直接清理
            DeleteMatchingFiles(root, extensions, cutoff, token,
                ref processed, ref deleted, dirsWithDeletedFiles);

            // 2) 子文件夹：只有整个子树全部是图片/CSV 才递归清理
            var topDirs = new List<string>();
            try
            {
                topDirs.AddRange(Directory.EnumerateDirectories(root));
            }
            catch (DirectoryNotFoundException) { }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }

            foreach (var subDir in topDirs)
            {
                if (token.IsCancellationRequested) break;

                try
                {
                    var attrs = File.GetAttributes(subDir);
                    if ((attrs & FileAttributes.ReparsePoint) != 0) continue;
                }
                catch
                {
                    skippedFolders++;
                    LogHelper.WriteDetail("跳过子文件夹（无法读取）：" + subDir);
                    continue;
                }

                // 先检查整个子文件夹（含所有下级）是否只有图片、CSV 和 TXT
                if (!IsRecursiveSafeFolder(subDir, token))
                {
                    if (!token.IsCancellationRequested)
                    {
                        skippedFolders++;
                        LogHelper.WriteDetail("跳过子文件夹（含其他格式）：" + subDir);
                    }
                    continue;
                }

                ProcessRecursiveSafeFolder(subDir, extensions, cutoff, token,
                    ref processed, ref deleted, dirsWithDeletedFiles);
            }

            // 3) 文件删空后的子文件夹一并删除（根目录保留）
            if (!token.IsCancellationRequested && dirsWithDeletedFiles.Count > 0)
            {
                removedFolders = DeleteEmptyParentFolders(root, dirsWithDeletedFiles, token);
            }

            return deleted;
        }

        /// <summary>
        /// 清理指定目录下匹配任务格式、且超过保留天数的文件
        /// </summary>
        private static void DeleteMatchingFiles(string dir, HashSet<string> extensions, DateTime cutoff,
            CancellationToken token, ref int processed, ref int deleted, HashSet<string> dirsWithDeletedFiles)
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(dir))
                {
                    if (token.IsCancellationRequested) break;

                    processed++;

                    // 每处理 200 个文件休息 20ms，避免占满磁盘和 CPU
                    if (processed % 200 == 0)
                    {
                        Thread.Sleep(20);
                    }

                    try
                    {
                        string ext = Path.GetExtension(file).TrimStart('.').ToLowerInvariant();
                        if (!extensions.Contains(ext)) continue;

                        var info = new FileInfo(file);
                        if (info.LastWriteTime > cutoff) continue;

                        File.Delete(file);
                        deleted++;
                        dirsWithDeletedFiles.Add(dir);
                        LogHelper.WriteDetail("删除文件：" + file);
                    }
                    catch (IOException)
                    {
                        // 文件被占用 / 被其他程序锁定，跳过
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // 没有权限，跳过
                    }
                    catch (Exception)
                    {
                        // 其他异常，跳过，不影响后续文件
                    }
                }
            }
            catch (DirectoryNotFoundException) { }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }

        /// <summary>
        /// 递归删除“图片/CSV/TXT”子文件夹内符合任务格式且过期的文件
        /// </summary>
        private static void ProcessRecursiveSafeFolder(string folder, HashSet<string> extensions, DateTime cutoff,
            CancellationToken token, ref int processed, ref int deleted, HashSet<string> dirsWithDeletedFiles)
        {
            var pendingDirs = new Stack<string>();
            pendingDirs.Push(folder);

            while (pendingDirs.Count > 0)
            {
                if (token.IsCancellationRequested) break;

                string dir = pendingDirs.Pop();

                try
                {
                    foreach (var subDir in Directory.EnumerateDirectories(dir))
                    {
                        if (token.IsCancellationRequested) break;

                        try
                        {
                            var attrs = File.GetAttributes(subDir);
                            if ((attrs & FileAttributes.ReparsePoint) != 0) continue;
                        }
                        catch
                        {
                            continue;
                        }

                        pendingDirs.Push(subDir);
                    }
                }
                catch (DirectoryNotFoundException) { }
                catch (UnauthorizedAccessException) { }
                catch (IOException) { }

                DeleteMatchingFiles(dir, extensions, cutoff, token,
                    ref processed, ref deleted, dirsWithDeletedFiles);
            }
        }

        /// <summary>
        /// 检查文件夹及其所有下级是否只包含图片、CSV 和 TXT 文件。
        /// 无法读取、存在其他格式时返回 false（保守跳过，不删除任何内容）。
        /// </summary>
        private static bool IsRecursiveSafeFolder(string folder, CancellationToken token)
        {
            var pendingDirs = new Stack<string>();
            pendingDirs.Push(folder);

            while (pendingDirs.Count > 0)
            {
                if (token.IsCancellationRequested) return false;

                string dir = pendingDirs.Pop();

                try
                {
                    foreach (var subDir in Directory.EnumerateDirectories(dir))
                    {
                        if (token.IsCancellationRequested) return false;

                        try
                        {
                            var attrs = File.GetAttributes(subDir);
                            if ((attrs & FileAttributes.ReparsePoint) != 0) continue;
                        }
                        catch
                        {
                            return false;
                        }

                        pendingDirs.Push(subDir);
                    }
                }
                catch (DirectoryNotFoundException) { return false; }
                catch (UnauthorizedAccessException) { return false; }
                catch (IOException) { return false; }

                try
                {
                    foreach (var file in Directory.EnumerateFiles(dir))
                    {
                        string ext = Path.GetExtension(file).TrimStart('.').ToLowerInvariant();
                        if (!RecursiveSafeExtensions.Contains(ext)) return false;
                    }
                }
                catch (DirectoryNotFoundException) { return false; }
                catch (UnauthorizedAccessException) { return false; }
                catch (IOException) { return false; }
            }

            return true;
        }

        /// <summary>
        /// 从删过文件的目录开始向上清理空文件夹，最多清理到任务根目录（不含根目录）
        /// </summary>
        private static int DeleteEmptyParentFolders(string root, HashSet<string> folders, CancellationToken token)
        {
            int removed = 0;
            string normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            foreach (var folder in folders.OrderByDescending(f => f.Length))
            {
                if (token.IsCancellationRequested) break;

                string current = folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                while (!string.IsNullOrEmpty(current) &&
                       current.Length > normalizedRoot.Length &&
                       current.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        if (Directory.EnumerateFileSystemEntries(current).Any()) break;
                        Directory.Delete(current, false);
                        removed++;
                        LogHelper.WriteDetail("删除空文件夹：" + current);
                    }
                    catch
                    {
                        break;
                    }

                    current = Path.GetDirectoryName(current);
                    if (!string.IsNullOrEmpty(current))
                    {
                        current = current.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    }
                }
            }

            return removed;
        }

        private static string FormatCleanExtra(int removedFolders, int skippedFolders)
        {
            string text = "";
            if (removedFolders > 0) text += $"，清理 {removedFolders} 个空文件夹";
            if (skippedFolders > 0) text += $"，跳过 {skippedFolders} 个含其他格式的子文件夹";
            return text;
        }

        private static List<string> ParseExtensions(string raw)
        {
            return (raw ?? string.Empty)
                .Split(new[] { ',', '，', ';', '；', '|', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim().TrimStart('.').ToLowerInvariant())
                .Where(s => s.Length > 0)
                .Distinct()
                .ToList();
        }

        // ==================== 运行状态与事件 ====================

        private bool TryBeginRun()
        {
            if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return false;

            try { RunStarted?.Invoke(); } catch { }
            return true;
        }

        private void EndRun(int totalDeleted)
        {
            Volatile.Write(ref _busy, 0);

            try { TaskCompleted?.Invoke(totalDeleted); } catch { }
        }

        private void RaiseLog(string message)
        {
            try { LogMessage?.Invoke(message); } catch { }
        }

        private void RaiseTasksChanged()
        {
            try { TasksChanged?.Invoke(); } catch { }
        }
    }
}
