using System;
using HotspotGo.Cli;
using HotspotGo.Core;
using HotspotGo.Logging;

namespace HotspotGo;

/// <summary>
/// 入口 + 组装根:解析参数、装配日志与业务服务、兜底顶层异常。
/// 不含任何业务逻辑。
///
/// 组件是 WinExe(运行时无窗口无控制台),log.txt 是唯一排障入口,
/// 因此启动 / 结束 / 任何致命异常都必须落日志。
///
/// 退出码语义见 <see cref="ExitCode"/>;业务流程见 <see cref="HotspotService"/>。
/// </summary>
internal static class Program
{
    /// <summary>日志文件名,落在 exe 同目录。</summary>
    private const string LogFileName = "log.txt";

    [STAThread]
    private static int Main(string[] args)
    {
        var logger = new AppendFileLogger(LogFileName);
        logger.WriteLine("===== 启动 (args: " + string.Join(" ", args) + ") =====");

        try
        {
            return (int)new HotspotService(logger).Run(Options.Parse(args));
        }
        catch (Exception ex)
        {
            logger.WriteLine("[FATAL] " + ex);
            return (int)ExitCode.Fatal;
        }
        finally
        {
            logger.WriteLine("===== 结束 =====");
        }
    }
}
