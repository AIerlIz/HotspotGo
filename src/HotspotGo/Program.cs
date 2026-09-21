using System;
using HotspotGo.Cli;
using HotspotGo.Core;
using HotspotGo.Logging;
using HotspotGo.WinRT;

namespace HotspotGo;

/// <summary>
/// 入口 + 组装根:解析参数、装配日志与业务服务、兜底顶层异常。
/// 不含任何业务逻辑。
///
/// 这是唯一"同时认识 Core 与 WinRT"的地方 —— Core 只定义它需要哪些接口
/// (<see cref="IConnectivityApi"/> / <see cref="ITetheringApi"/>),
/// 真实实现在这里接上去,所以依赖方向是单向朝内的。
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

        return Execute(logger, args, () => (int)new HotspotService(
            logger, new SystemConnectivityApi(), new SystemTetheringApi()).Run(Options.Parse(args)));
    }

    /// <summary>
    /// 启动 / 结束日志 + 顶层异常兜底。
    ///
    /// 抽成独立方法是为了能测(见 tests/ProgramTests):它保证"不管出什么事,
    /// log.txt 里都有这次运行的首尾两行 + 致命异常原样留档",而这正是排障的前提。
    /// </summary>
    /// <param name="logger">日志器。</param>
    /// <param name="args">原始命令行参数,原样回显到启动行,便于排查手误。</param>
    /// <param name="body">真正的运行主体,返回进程退出码。</param>
    internal static int Execute(ILogger logger, string[] args, Func<int> body)
    {
        logger.WriteLine("===== 启动 (args: " + string.Join(" ", args) + ") =====");

        try
        {
            return body();
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
