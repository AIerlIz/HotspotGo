using System;
using System.Collections.Generic;

namespace HotspotGo.Cli;

/// <summary>要执行的热点操作。</summary>
internal enum HotspotCommand
{
    /// <summary>等上游就绪后开启热点(默认)。</summary>
    TurnOn,

    /// <summary>关闭热点。</summary>
    TurnOff,

    /// <summary>只读状态,不做任何改动。</summary>
    Status,
}

/// <summary>
/// 命令行选项。用法:
/// <code>
///   HotspotGo.exe              等上游就绪后开启热点(默认最多等 90 秒)
///   HotspotGo.exe --any        不等外网,直接用当前可用连接开启热点
///   HotspotGo.exe --status     只看状态,不做任何改动
///   HotspotGo.exe --off        关闭热点
///   HotspotGo.exe --wait 120   自定义最长等待秒数(0 - 86400)
///   HotspotGo.exe --help       显示用法说明
/// </code>
/// 解析策略 —— 三条都是为同一个目标服务的:<b>别因为手误把命令执行反了</b>
/// (本工具"不带参数"就是开热点,任何回落成默认动作的解析歧义都会让
/// <c>--off</c> 敲错一个字就变成开热点)。
///   <list type="bullet">
///     <item>无法识别的参数<b>记下来</b>(见 <see cref="UnknownArguments"/>),由
///       <c>Program.Preflight</c> 拒绝执行,而不是静默忽略。</item>
///     <item><c>--wait</c> 的取值只吃"看起来像取值"的 token:下一个参数是
///       <c>--status</c> 这类开关时必须留给下一轮,否则
///       <c>HotspotGo.exe --wait --status</c> 会把只读命令吞成开热点。</item>
///     <item><c>--wait</c> 取值非法(非数字 / 负数 / 超过上限)时消费掉该 token,
///       但保持默认值(不抛异常,本次运行照常进行)。</item>
///   </list>
/// 本类只做解析,不做判定,也不抛异常 —— 拒绝执行是 <c>Program.Preflight</c> 的事。
/// </summary>
internal sealed class Options
{
    /// <summary>默认最长等待上游秒数。</summary>
    public const int DefaultMaxWaitSeconds = 90;

    /// <summary>
    /// <c>--wait</c> 允许的最大秒数(24 小时)。
    ///
    /// 为什么要有上限:本工具会被放进登录脚本 / 计划任务,而"等上游"是个前台阻塞等待。
    /// 多敲一个 0(比如 <c>--wait 9000</c> 想写 900)能把登录拖住几小时,
    /// 这比"拒绝一个离谱取值、退回默认的 90 秒"糟糕得多。
    /// </summary>
    public const int MaxAllowedWaitSeconds = 86400;

    private readonly List<string> _unknownArguments = new List<string>();

    /// <summary>要执行的操作。</summary>
    public HotspotCommand Command { get; private set; }

    /// <summary>最长等待上游就绪的秒数。</summary>
    public int MaxWaitSeconds { get; private set; } = DefaultMaxWaitSeconds;

    /// <summary>
    /// true = 不等外网,直接用当前可用连接开启热点(对应 <c>--any</c>)。
    /// 仅在开启热点时生效。
    /// </summary>
    public bool UseAnyConnection { get; private set; }

    /// <summary>
    /// 是否请求用法说明(对应 <c>--help</c> / <c>-h</c> / <c>-?</c> / <c>/?</c>)。
    ///
    /// 刻意独立于 <see cref="Command"/>:说明请求出现一次就该出说明,
    /// 不跟着"同一开关出现多次时后者生效"那条规则走 ——
    /// <c>--help --status</c> 里用户想看的显然是说明。
    /// </summary>
    public bool HelpRequested { get; private set; }

    /// <summary>
    /// 无法识别的参数(按出现顺序)。
    /// 非空时不允许执行任何操作 —— 见 <c>Program.Preflight</c>。
    /// </summary>
    public IReadOnlyList<string> UnknownArguments => _unknownArguments;

    public static Options Parse(string[] args)
    {
        var options = new Options();

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--status":
                    options.Command = HotspotCommand.Status;
                    break;

                case "--off":
                    options.Command = HotspotCommand.TurnOff;
                    break;

                case "--any":
                    options.UseAnyConnection = true;
                    break;

                case "--help":
                case "-h":
                case "-?":
                case "/?":
                    options.HelpRequested = true;
                    break;

                case "--wait":
                    // 取值开关消费紧随的 token;解析失败也消费,但保持默认值
                    if (i + 1 < args.Length && LooksLikeValue(args[i + 1]))
                    {
                        i++;
                        if (int.TryParse(args[i], out int seconds) &&
                            seconds >= 0 && seconds <= MaxAllowedWaitSeconds)
                            options.MaxWaitSeconds = seconds;
                    }
                    break;

                default:
                    // 不在这里报错/退出:解析保持纯粹(纯状态变更),拒绝执行交给 Program.Preflight。
                    // 这样"参数长什么样"和"怎么处理参数"能各自单独测。
                    options._unknownArguments.Add(args[i]);
                    break;
            }
        }

        return options;
    }

    /// <summary>
    /// 下一个 token 能不能当作 <c>--wait</c> 的取值:
    /// <list type="bullet">
    ///     <item>不以 <c>-</c> 开头的一律算 —— 含空串、<c>abc</c>、<c>12.5</c>:
    ///       交给 <see cref="int.TryParse(string, out int)"/> 判成非法并保持默认值(原有约定)。</item>
    ///     <item>以 <c>-</c> 开头时只有整数才算 —— 这样 <c>--wait -5</c> 能走到
    ///       "取值非法、保持默认值",而 <c>--wait --status</c>、<c>--wait -h</c>
    ///       里的开关不会被当成取值吞掉。</item>
    /// </list>
    /// </summary>
    private static bool LooksLikeValue(string token)
        => !token.StartsWith("-", StringComparison.Ordinal) || int.TryParse(token, out _);
}
