using HotspotGo.Cli;
using Xunit;

namespace HotspotGo.Tests.Cli;

/// <summary>
/// 命令行参数解析。
///
/// 这是本工具对外的唯一接口面 —— README 的命令表、用户的计划任务、脚本都直接依赖它,
/// 所以每个开关的语义都锁死在这里。
///
/// 其中两条是"别因为手误把命令执行反了"的锁:本工具不带参数就是开热点,
/// 所以 <c>--wait</c> 不能吞掉后面的开关,无法识别的参数也必须被记下来
/// (由 <c>Program.Preflight</c> 拒绝执行)。
/// </summary>
public class OptionsTests
{
    /// <summary>无参 = 默认行为:等上游就绪后开启热点,用默认等待秒数,不启用 --any。</summary>
    [Fact]
    public void Parse_no_args_defaults_to_turn_on()
    {
        var options = Options.Parse(new string[0]);

        Assert.Equal(HotspotCommand.TurnOn, options.Command);
        Assert.Equal(Options.DefaultMaxWaitSeconds, options.MaxWaitSeconds);
        Assert.False(options.UseAnyConnection);
        Assert.False(options.HelpRequested);
        Assert.Empty(options.UnknownArguments);
    }

    /// <summary>默认等待秒数是 90(README 里写明的值)。</summary>
    [Fact]
    public void Default_max_wait_is_ninety_seconds()
    {
        Assert.Equal(90, Options.DefaultMaxWaitSeconds);
    }

    /// <summary>--status / --off / --any 各自映射到对应命令。</summary>
    [Fact]
    public void Parse_maps_command_switches()
    {
        Assert.Equal(HotspotCommand.Status, Options.Parse(new[] { "--status" }).Command);
        Assert.Equal(HotspotCommand.TurnOff, Options.Parse(new[] { "--off" }).Command);
        Assert.Equal(HotspotCommand.TurnOn, Options.Parse(new[] { "--any" }).Command);
    }

    /// <summary>--any 只置标志,不改变命令本身。</summary>
    [Fact]
    public void Parse_any_sets_flag_without_changing_command()
    {
        var options = Options.Parse(new[] { "--any" });

        Assert.True(options.UseAnyConnection);
        Assert.Equal(HotspotCommand.TurnOn, options.Command);
    }

    /// <summary>--wait N 覆盖默认等待秒数。</summary>
    [Fact]
    public void Parse_wait_overrides_timeout()
    {
        Assert.Equal(30, Options.Parse(new[] { "--wait", "30" }).MaxWaitSeconds);
    }

    /// <summary>--wait 取值非法时消费掉该 token,但保持默认值(不抛异常)。</summary>
    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("12.5")]
    [InlineData("30s")]
    public void Parse_wait_with_invalid_value_keeps_default(string badValue)
    {
        var options = Options.Parse(new[] { "--wait", badValue });

        Assert.Equal(Options.DefaultMaxWaitSeconds, options.MaxWaitSeconds);
        // 取值位置上的 token 是"被消费"的,不该再被算成无法识别的参数
        Assert.Empty(options.UnknownArguments);
    }

    /// <summary>
    /// 超出范围的取值同样按非法处理,并且消费掉该 token。
    ///
    /// 上限不是洁癖:本工具会被放进登录脚本,而"等上游"是前台阻塞等待,
    /// 多敲一个 0 能把登录拖住几小时,比"退回默认的 90 秒"糟糕得多。
    /// </summary>
    [Theory]
    [InlineData("-5")]
    [InlineData("-1")]
    [InlineData("86401")]
    [InlineData("999999999")]
    public void Parse_wait_out_of_range_keeps_default(string badValue)
    {
        var options = Options.Parse(new[] { "--wait", badValue });

        Assert.Equal(Options.DefaultMaxWaitSeconds, options.MaxWaitSeconds);
        Assert.Empty(options.UnknownArguments);
    }

    /// <summary>边界值要能用:0(只查一次)和上限(24 小时)。</summary>
    [Theory]
    [InlineData("0")]
    [InlineData("90")]
    [InlineData("86400")]
    public void Parse_wait_accepts_values_in_range(string value)
    {
        Assert.Equal(int.Parse(value), Options.Parse(new[] { "--wait", value }).MaxWaitSeconds);
    }

    /// <summary>--wait 位于参数末尾(后面没有值)时不越界,保持默认值。</summary>
    [Fact]
    public void Parse_wait_without_following_value_keeps_default()
    {
        Assert.Equal(Options.DefaultMaxWaitSeconds, Options.Parse(new[] { "--wait" }).MaxWaitSeconds);
    }

    /// <summary>
    /// --wait 后面跟着另一个开关时,不能把那个开关当取值吃掉。
    ///
    /// 否则 <c>HotspotGo.exe --wait --status</c> 会把只读命令吞成"开热点",
    /// 而且退出码还是 0 —— 用户以为在看状态,实际改动了系统。
    /// </summary>
    [Fact]
    public void Parse_wait_does_not_swallow_a_following_switch()
    {
        var options = Options.Parse(new[] { "--wait", "--status" });

        Assert.Equal(HotspotCommand.Status, options.Command);
        Assert.Equal(Options.DefaultMaxWaitSeconds, options.MaxWaitSeconds);
        Assert.Empty(options.UnknownArguments);
    }

    /// <summary>短开关(--help 的等价写法)同样不能被 --wait 吞掉。</summary>
    [Fact]
    public void Parse_wait_does_not_swallow_help()
    {
        var options = Options.Parse(new[] { "--wait", "-h" });

        Assert.True(options.HelpRequested);
        Assert.Equal(Options.DefaultMaxWaitSeconds, options.MaxWaitSeconds);
        Assert.Empty(options.UnknownArguments);
    }

    /// <summary>同一开关出现多次时,最后出现的生效。</summary>
    [Fact]
    public void Parse_last_occurrence_wins()
    {
        Assert.Equal(HotspotCommand.TurnOff, Options.Parse(new[] { "--status", "--off" }).Command);
        Assert.Equal(15, Options.Parse(new[] { "--wait", "60", "--wait", "15" }).MaxWaitSeconds);
    }

    /// <summary>
    /// 无法识别的参数被记下来(而不是静默忽略),由 <c>Program.Preflight</c> 拒绝执行 ——
    /// 否则 <c>--pff</c>(<c>--off</c> 敲错)会落到"开热点"这个默认动作上。
    /// 解析本身仍然照常产出结果,好在测试里把"解析"和"怎么处理"分开验。
    /// </summary>
    [Fact]
    public void Parse_records_unknown_arguments()
    {
        var options = Options.Parse(new[] { "-x", "--unknown", "stray", "--wait", "15", "--status" });

        Assert.Equal(HotspotCommand.Status, options.Command);
        Assert.Equal(15, options.MaxWaitSeconds);
        Assert.Equal(new[] { "-x", "--unknown", "stray" }, options.UnknownArguments);
    }

    /// <summary>--help 的四种写法都算"要看说明"。</summary>
    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("-?")]
    [InlineData("/?")]
    public void Parse_recognizes_help_switches(string flag)
    {
        Assert.True(Options.Parse(new[] { flag }).HelpRequested);
    }

    /// <summary>
    /// 单横线的<b>长</b>写法不认:<c>-status</c> / <c>-wait</c> / <c>-off</c> 一律按无法识别处理。
    ///
    /// 一套开关只给两种拼法 —— 长开关两个横线、短写一个横线,而且短写已经把同样的意图都覆盖了。
    /// 多留一种"看起来也行"的写法,只会让人分不清到底哪套才算数;真敲错了也有退出码 5 和用法说明接着。
    /// </summary>
    [Theory]
    [InlineData("-status")]
    [InlineData("-wait")]
    [InlineData("-off")]
    [InlineData("-any")]
    [InlineData("-help")]
    [InlineData("-version")]
    public void Parse_rejects_single_dash_long_switches(string flag)
    {
        Assert.Equal(new[] { flag }, Options.Parse(new[] { flag }).UnknownArguments);
    }

    /// <summary>
    /// 六个首字母短写:各归各的开关,互不串味。
    /// 短写是显式列出来的,不是"取长开关首字母"推出来的 —— 后者在以后加开关时会悄悄改含义。
    /// </summary>
    [Fact]
    public void Parse_accepts_short_switches()
    {
        Assert.Equal(HotspotCommand.Status, Options.Parse(new[] { "-s" }).Command);
        Assert.Equal(HotspotCommand.TurnOff, Options.Parse(new[] { "-o" }).Command);
        Assert.True(Options.Parse(new[] { "-a" }).UseAnyConnection);
        Assert.Equal(30, Options.Parse(new[] { "-w", "30" }).MaxWaitSeconds);
        Assert.True(Options.Parse(new[] { "-h" }).HelpRequested);
        Assert.True(Options.Parse(new[] { "-v" }).VersionRequested);
    }

    /// <summary>短写不能被算成"无法识别的参数" —— 它们本来就是开关,不该触发退出码 5。</summary>
    [Fact]
    public void Parse_records_no_unknown_arguments_for_short_switches()
    {
        var options = Options.Parse(new[] { "-s", "-a", "-w", "15" });

        Assert.Empty(options.UnknownArguments);
        Assert.Equal(15, options.MaxWaitSeconds);
    }

    /// <summary>--wait 不能吞掉后面的开关:长写短写、混着写都不行。</summary>
    [Theory]
    [InlineData("--wait --status")]
    [InlineData("--wait -s")]
    [InlineData("-w --status")]
    [InlineData("-w -s")]
    public void Parse_wait_never_swallows_a_following_switch(string commandLine)
    {
        var options = Options.Parse(commandLine.Split(' '));

        Assert.Equal(HotspotCommand.Status, options.Command);
        Assert.Equal(Options.DefaultMaxWaitSeconds, options.MaxWaitSeconds);
        Assert.Empty(options.UnknownArguments);
    }

    /// <summary>版本请求只认 --version 与 -v,不牵连别的开关。</summary>
    [Fact]
    public void Parse_recognizes_version_switches()
    {
        Assert.True(Options.Parse(new[] { "--version" }).VersionRequested);
        Assert.True(Options.Parse(new[] { "-v" }).VersionRequested);
        Assert.False(Options.Parse(new[] { "-s" }).VersionRequested);
    }

    /// <summary>拼错的名字要报错,而且报的是用户真正敲的那个写法(不做归一化、也不猜)。</summary>
    [Fact]
    public void Parse_still_rejects_unknown_arguments()
    {
        var options = Options.Parse(new[] { "-x", "-stauts", "--stauts" });

        Assert.Equal(new[] { "-x", "-stauts", "--stauts" }, options.UnknownArguments);
    }

    /// <summary>
    /// 说明请求独立于命令:出现在参数里任意位置都算数,
    /// 不跟着"同一开关出现多次时后者生效"那条规则走(<c>--help --status</c> 要看的是说明)。
    /// </summary>
    [Fact]
    public void Parse_help_is_independent_of_the_command()
    {
        var options = Options.Parse(new[] { "--help", "--status" });

        Assert.True(options.HelpRequested);
        Assert.Equal(HotspotCommand.Status, options.Command);
        Assert.Empty(options.UnknownArguments);
    }

    /// <summary>参数顺序不影响解析结果。</summary>
    [Fact]
    public void Parse_is_order_independent()
    {
        var first = Options.Parse(new[] { "--any", "--wait", "45", "--off" });
        var second = Options.Parse(new[] { "--off", "--any", "--wait", "45" });

        Assert.Equal(first.Command, second.Command);
        Assert.Equal(first.MaxWaitSeconds, second.MaxWaitSeconds);
        Assert.Equal(first.UseAnyConnection, second.UseAnyConnection);
    }
}
