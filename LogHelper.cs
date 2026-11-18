using System;
using System.IO;
using System.Text;

namespace AutoDeleteImage
{
    /// <summary>
    /// 运行日志：按天保存到程序启动目录下的 Log 文件夹。
    /// 保持文件句柄并批量刷新，逐文件记录时也不会频繁开关文件。
    /// 写日志失败时静默忽略，不影响程序正常运行。
    /// </summary>
    public static class LogHelper
    {
        private static readonly object _lock = new object();
        private static readonly UTF8Encoding _encoding = new UTF8Encoding(true);

        private static readonly string _logDirectory =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Log");

        private static StreamWriter _writer;
        private static DateTime _writerDate = DateTime.MinValue;
        private static int _pendingLines = 0;

        public static string LogDirectory
        {
            get { return _logDirectory; }
        }

        public static string CurrentLogFile
        {
            get { return Path.Combine(_logDirectory, DateTime.Now.ToString("yyyy-MM-dd") + ".log"); }
        }

        /// <summary>
        /// 普通日志：立即写入并刷新（任务摘要、启动退出、错误等）
        /// </summary>
        public static void Write(string message)
        {
            WriteCore(message, true);
        }

        /// <summary>
        /// 详细日志：写入文件但批量刷新（逐文件删除记录等，量大）
        /// </summary>
        public static void WriteDetail(string message)
        {
            WriteCore(message, false);
        }

        public static void Flush()
        {
            lock (_lock)
            {
                try
                {
                    if (_writer != null)
                    {
                        _writer.Flush();
                        _pendingLines = 0;
                    }
                }
                catch { }
            }
        }

        /// <summary>
        /// 关闭日志文件（程序退出时调用）
        /// </summary>
        public static void Close()
        {
            lock (_lock)
            {
                CloseWriter();
            }
        }

        private static void WriteCore(string message, bool forceFlush)
        {
            lock (_lock)
            {
                try
                {
                    DateTime now = DateTime.Now;
                    EnsureWriter(now);

                    string line = "[" + now.ToString("yyyy-MM-dd HH:mm:ss") + "] "
                                  + (message ?? string.Empty);

                    _writer.WriteLine(line);
                    _pendingLines++;

                    // 普通日志立即落盘；详细日志每 20 条刷新一次
                    if (forceFlush || _pendingLines >= 20)
                    {
                        _writer.Flush();
                        _pendingLines = 0;
                    }
                }
                catch
                {
                    CloseWriter();
                }
            }
        }

        private static void EnsureWriter(DateTime now)
        {
            if (_writer != null && _writerDate.Date == now.Date) return;

            CloseWriter();

            Directory.CreateDirectory(_logDirectory);

            string file = Path.Combine(_logDirectory, now.ToString("yyyy-MM-dd") + ".log");
            var stream = new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.Read);
            _writer = new StreamWriter(stream, _encoding);
            _writerDate = now.Date;
        }

        private static void CloseWriter()
        {
            if (_writer != null)
            {
                try { _writer.Flush(); } catch { }
                try { _writer.Dispose(); } catch { }
                _writer = null;
            }

            _pendingLines = 0;
        }
    }
}
