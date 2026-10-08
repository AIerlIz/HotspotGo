using System;
using HotspotGo.Cli;
using HotspotGo.Core;
using HotspotGo.Logging;
using HotspotGo.WinRT;

namespace HotspotGo;

/// <summary>
/// 入口 + 组装根:解析参数、装配日志与业务服务、兜底顶层异常。
/// 除参数层面的分流(<see cref="Preflight"/>)外不含任何业务逻辑。
///
/// 这是唯一"同时认识 Core 与 WinRT"的地方 —— Core 只定义它需要哪些接口
/// (<see cref="IConnectivityApi"/> / <see cref="ITetheringApi"/>),
/// 真实实现在这里接上去,所以依赖方向是单向朝内的。
///
/// 组件是 WinExe(运行时无窗口无控制台),log.txt 是唯一排障入口,
/// 因此启动 / 结束 / 任何致命异常都必须落日志 —— 用法说明也一样写进日志。
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
        var options = Options.Parse(args);

        return Execute(logger, args, () =>
        {
            ExitCode? preflight = Preflight(logger, options);
            if (preflight.HasValue) return (int)preflight.Value;

            return (int)new HotspotService(
                logger, new SystemConnectivityApi(), new SystemTetheringApi()).Run(options);
        });
    }

    /// <summary>
    /// 参数层面的分流:该不该执行、退出码是什么。返回 null = 参数没问题,交给业务服务。
    ///
    /// 为什么必须有这一步:本工具不带参数就是"开热点",所以任何无法识别的参数都必须
    /// 在这里被拦住,而不是回落成默认动作 —— 否则 <c>--pff</c>(<c>--off</c> 敲错)
    /// 会反过来把热点开起来,方向正好相反。
    ///
    /// <c>--help</c> 优先于未知参数:用户明确要看说明时,说明比报错有用。
    /// 说明写进 log.txt(无控制台,这是唯一出口,见 <see cref="UsageText"/>)。
    /// </summary>
    /// <returns>该就地结束时的退出码;null 表示继续执行业务流程。</returns>
    internal static ExitCode? Preflight(ILogger logger, Options options)
    {
        if (options.HelpRequested)
        {
            logger.WriteLine("[帮助] 用法说明:");
            WriteUsage(logger);
            return ExitCode.Ok;
        }

        if (options.UnknownArguments.Count > 0)
        {
            logger.WriteLine("[失败] 无法识别的参数: " + string.Join(" ", options.UnknownArguments) +
                "。为避免手误把命令执行反了(比如把 --off 敲成 --pff)," +
                "本次不做任何操作。");
            WriteUsage(logger);
            return ExitCode.BadArguments;
        }

        return null;
    }

    /// <summary>逐行写用法说明 —— 每行单独写,好让每行都带上时间戳。</summary>
    private static void WriteUsage(ILogger logger)
    {
        foreach (var line in UsageText.Lines) logger.WriteLine(line);
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
