using System;
using System.IO;
using HotspotGo.Logging;
using Xunit;

namespace HotspotGo.Tests.Logging;

/// <summary>
/// 日志器。
///
/// 本程序是 WinExe(无窗口无控制台),log.txt 是唯一排障入口,因此它有两条
/// 必须成立的约定:追加写(不覆盖历史)、以及写入失败静默(绝不中断主流程)。
/// </summary>
public class AppendFileLoggerTests
{
    /// <summary>日志落在 exe 同目录,测试进程里就是测试输出目录。</summary>
    private static string PathOf(string fileName) => Path.Combine(AppContext.BaseDirectory, fileName);

    /// <summary>生成一个不会和别的测试/历史文件撞名的日志名。</summary>
    private static string UniqueName() => "hotspotgo-test-" + Guid.NewGuid().ToString("N") + ".txt";

    /// <summary>每行都带 yyyy-MM-dd HH:mm:ss 时间戳前缀 —— 事后定位问题靠它。</summary>
    [Fact]
    public void WriteLine_writes_timestamped_line()
    {
        var name = UniqueName();
        try
        {
            new AppendFileLogger(name).WriteLine("一条日志");

            var lines = File.ReadAllLines(PathOf(name));

            Assert.Single(lines);
            Assert.Contains("一条日志", lines[0]);
            Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}  ", lines[0]);
        }
        finally
        {
            DeleteQuietly(name);
        }
    }

    /// <summary>追加写:多次写入不覆盖已有内容(计划任务与手动启动会并发写同一个文件)。</summary>
    [Fact]
    public void WriteLine_appends_instead_of_overwriting()
    {
        var name = UniqueName();
        try
        {
            var logger = new AppendFileLogger(name);
            logger.WriteLine("第一次");
            logger.WriteLine("第二次");
            logger.WriteLine("第三次");

            var lines = File.ReadAllLines(PathOf(name));

            Assert.Equal(3, lines.Length);
            Assert.Contains("第一次", lines[0]);
            Assert.Contains("第三次", lines[2]);
        }
        finally
        {
            DeleteQuietly(name);
        }
    }

    /// <summary>文件被占用时也能写进去(以 FileShare.ReadWrite 打开,不长期占 handle)。</summary>
    [Fact]
    public void WriteLine_succeeds_while_file_is_held_open()
    {
        var name = UniqueName();
        try
        {
            new AppendFileLogger(name).WriteLine("占位");

            using (new FileStream(PathOf(name), FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                Assert.Null(Record.Exception(() => new AppendFileLogger(name).WriteLine("并发写入")));
            }

            Assert.Equal(2, File.ReadAllLines(PathOf(name)).Length);
        }
        finally
        {
            DeleteQuietly(name);
        }
    }

    /// <summary>
    /// 写入失败必须静默 —— 绝不因为"记日志"这件小事中断"开热点"这个主流程。
    /// 这里让日志名撞上一个同名目录,使打开文件必然失败。
    /// </summary>
    [Fact]
    public void WriteLine_swallows_io_failures()
    {
        var name = "hotspotgo-test-dir-" + Guid.NewGuid().ToString("N");
        var directory = PathOf(name);
        Directory.CreateDirectory(directory);
        try
        {
            var logger = new AppendFileLogger(name);

            Assert.Null(Record.Exception(() => logger.WriteLine("不该抛出")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void DeleteQuietly(string fileName)
    {
        try
        {
            var path = PathOf(fileName);
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            /* 清理失败不影响测试结论 */
        }
    }
}
