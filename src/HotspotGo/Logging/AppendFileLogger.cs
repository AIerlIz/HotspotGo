using System;
using System.IO;
using System.Text;

namespace HotspotGo.Logging;

/// <summary>
/// 历史日志器:追加写 exe 同目录下的文件,每行带时间戳,不打印控制台。
///
/// 本组件是 WinExe(没有控制台),日志是唯一的排障手段,所以约定:
///   - 写入失败静默处理 —— 绝不因为"记日志"这件小事中断"开热点"这个主流程;
///   - 以 <see cref="FileShare.ReadWrite"/> 打开,允许多实例(计划任务 + 手动)同时写;
///   - 每次都独立开关文件流、写完即关,不长期占着 handle。
/// </summary>
internal sealed class AppendFileLogger : ILogger
{
    private readonly string _logFilePath;

    /// <param name="fileName">日志文件名,落在 exe 同目录(见 <see cref="AppContext.BaseDirectory"/>)。</param>
    public AppendFileLogger(string fileName)
    {
        _logFilePath = Path.Combine(AppContext.BaseDirectory, fileName);
    }

    /// <summary>追加一行带时间戳的日志。写入失败静默处理,绝不抛出。</summary>
    public void WriteLine(string message)
    {
        try
        {
            var directory = Path.GetDirectoryName(_logFilePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine;
            using var stream = new FileStream(_logFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            var bytes = new UTF8Encoding(false).GetBytes(line);
            stream.Write(bytes, 0, bytes.Length);
        }
        catch
        {
            /* 静默 — 日志失败不应中断主流程 */
        }
    }
}
