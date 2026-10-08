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
/// 日志同时去两个地方:exe 同目录的 log.txt(永远是权威记录 —— 终端一关就没了,
/// 而本工具要在事发之后还能查),以及终端(有终端在看时,见 <see cref="Terminal"/>)。
/// log.txt 优先:启动 / 结束 / 任何致命异常都必须落进它,用法说明也一样。
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
        // 第一件事,必须在任何输出之前:判断这次有没有人在看终端,
        // 并把"系统为本次启动新建的控制台窗口"藏掉(双击 / 启动文件夹)。
        // 判定结果显式往下传,不藏在静态状态里 —— 数据流一眼看得出,也不用猜谁改过它。
        TerminalSetup terminal = Terminal.Prepare();

        ILogger logger = BuildLogger(terminal);
        var options = Options.Parse(args);

        return Execute(logger, args, () =>
        {
            // 把"这次对终端做了什么"也记一笔:双击 / 开机自启时终端上什么都没显示,
            // 事后能回答"为什么没反应"的只有这一行(见 TerminalSetup.Describe)。
            logger.WriteLine("  终端输出:" + (terminal.WriteToTerminal ? "开" : "关") +
                "(" + terminal.Describe() + ")");

            ExitCode? preflight = Preflight(logger, options);
            if (preflight.HasValue) return (int)preflight.Value;

            return (int)new HotspotService(
                logger, new SystemConnectivityApi(), new SystemTetheringApi()).Run(options);
        });
    }

    /// <summary>
    /// 装配日志:log.txt 永远写;有人在看终端时再镜像一份过去。
    /// 没有终端时(见 <see cref="TerminalSetup.WriteToTerminal"/>)只剩文件那份 ——
    /// 这正是双击 / 开机自启时的样子:不打扰,但事后有据可查。
    /// </summary>
    private static ILogger BuildLogger(TerminalSetup terminal)
    {
        var fileLog = new AppendFileLogger(LogFileName);

        return terminal.WriteToTerminal
            ? new MirrorLogger(fileLog, new ConsoleLogger())
            : (ILogger)fileLog;
    }

    /// <summary>
    /// 参数层面的分流:该不该执行、退出码是什么。返回 null = 参数没问题,交给业务服务。
    ///
    /// 为什么必须有这一步:本工具不带参数就是"开热点",所以任何无法识别的参数都必须
    /// 在这里被拦住,而不是回落成默认动作 —— 否则 <c>--pff</c>(<c>--off</c> 敲错)
    /// 会反过来把热点开起来,方向正好相反。
    ///
    /// <c>--help</c> 与 <c>--version</c> 优先于未知参数:用户明确要看说明 / 版本时,
    /// 那比报错有用。两者都写进 log.txt(说明见 <see cref="UsageText"/>、<see cref="VersionText"/>)。
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

        if (options.VersionRequested)
        {
            // 版本号取自程序集自身(发布时按 tag 注入),不在代码里另写一份常量 ——
            // 否则迟早会与 exe 文件属性对不上。见 VersionText。
            foreach (var line in VersionText.Build()) logger.WriteLine(line);
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
