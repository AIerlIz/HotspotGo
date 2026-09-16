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
///   HotspotGo.exe --wait 120   自定义最长等待秒数
/// </code>
/// 解析策略:未知参数静默忽略(启动日志会原样记录 args,便于排查手误);
/// <c>--wait</c> 的取值非法时消费掉该 token 但保持默认值。
/// </summary>
internal sealed class Options
{
    /// <summary>默认最长等待上游秒数。</summary>
    public const int DefaultMaxWaitSeconds = 90;

    /// <summary>要执行的操作。</summary>
    public HotspotCommand Command { get; private set; }

    /// <summary>最长等待上游就绪的秒数。</summary>
    public int MaxWaitSeconds { get; private set; } = DefaultMaxWaitSeconds;

    /// <summary>
    /// true = 不等外网,直接用当前可用连接开启热点(对应 <c>--any</c>)。
    /// 仅在开启热点时生效。
    /// </summary>
    public bool UseAnyConnection { get; private set; }

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

                case "--wait":
                    // 取值开关消费紧随的 token;解析失败也消费,但保持默认值
                    if (i + 1 < args.Length && int.TryParse(args[++i], out int seconds))
                        options.MaxWaitSeconds = seconds;
                    break;
            }
        }

        return options;
    }
}
