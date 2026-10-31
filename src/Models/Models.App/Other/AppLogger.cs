// Copyright (c) Richasy. All rights reserved.

using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Bili.Models.App.Other
{
    /// <summary>
    /// 轻量本地日志。写在应用本地数据目录下的 BiliDiag 文件夹，
    /// 便于在真机上排查登录/鉴权等无法通过界面观察到的问题。
    /// </summary>
    public static class AppLogger
    {
        private static readonly object SyncRoot = new object();
        private static string _logFile;

        /// <summary>
        /// 日志目录（首次写入后可用）。
        /// </summary>
        public static string LogDirectory { get; private set; }

        /// <summary>
        /// 显式指定日志根目录（UWP 侧传入 ApplicationData.Current.LocalFolder.Path，
        /// 避免容器重定向导致日志写到不可预期的位置）。
        /// </summary>
        /// <param name="baseDirectory">根目录。</param>
        public static void Initialize(string baseDirectory)
        {
            try
            {
                var dir = Path.Combine(baseDirectory, "BiliDiag");
                Directory.CreateDirectory(dir);
                LogDirectory = dir;
                CleanupOldLogs(dir);
                var name = "bili-" + DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".log";
                _logFile = Path.Combine(dir, name);
                Write("INFO", "App", "日志系统已初始化: " + _logFile);
            }
            catch
            {
                _logFile = null;
            }
        }

        /// <summary>
        /// 清理超过 7 天的历史日志，避免长期使用后占用空间。
        /// </summary>
        /// <summary>
        /// 记录一般信息。
        /// </summary>
        /// <param name="tag">模块标记。</param>
        /// <param name="message">内容。</param>
        public static void Info(string tag, string message)
        {
            Write("INFO", tag, message);
        }

        /// <summary>
        /// 记录警告。
        /// </summary>
        /// <param name="tag">模块标记。</param>
        /// <param name="message">内容。</param>
        public static void Warn(string tag, string message)
        {
            Write("WARN", tag, message);
        }

        /// <summary>
        /// 记录错误。
        /// </summary>
        /// <param name="tag">模块标记。</param>
        /// <param name="message">内容。</param>
        /// <param name="exception">异常对象，可为空。</param>
        public static void Error(string tag, string message, Exception exception = null)
        {
            var text = exception == null
                ? message
                : string.Format(CultureInfo.InvariantCulture, "{0} | {1}: {2}", message, exception.GetType().Name, exception.Message);
            Write("ERROR", tag, text);

            if (exception?.StackTrace != null)
            {
                Write("ERROR", tag, "  " + exception.StackTrace.Replace(Environment.NewLine, " <- "));
            }
        }

        private static void Write(string level, string tag, string message)
        {
            try
            {
                if (_logFile == null)
                {
                    Init();
                }

                if (_logFile == null)
                {
                    return;
                }

                var line = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0:yyyy-MM-dd HH:mm:ss.fff} [{1}] [{2}] {3}",
                    DateTimeOffset.Now,
                    level,
                    tag,
                    message);

                lock (SyncRoot)
                {
                    File.AppendAllText(_logFile, line + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch
            {
                // 日志本身不允许影响业务
            }
        }

        private static void Init()
        {
            try
            {
                var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var dir = Path.Combine(root, "BiliDiag");
                Directory.CreateDirectory(dir);
                LogDirectory = dir;

                var name = "bili-" + DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".log";
                _logFile = Path.Combine(dir, name);
            }
            catch
            {
                _logFile = null;
            }
        }

        /// <summary>
        /// 清理超过 7 天的历史日志，避免长期使用后占用空间。
        /// </summary>
        /// <param name="directory">日志目录。</param>
        private static void CleanupOldLogs(string directory)
        {
            try
            {
                var deadline = DateTime.Now.AddDays(-7);
                foreach (var file in Directory.GetFiles(directory, "bili-*.log"))
                {
                    if (File.GetLastWriteTime(file) < deadline)
                    {
                        File.Delete(file);
                    }
                }
            }
            catch
            {
                // 清理失败不影响主流程
            }
        }
    }
}
