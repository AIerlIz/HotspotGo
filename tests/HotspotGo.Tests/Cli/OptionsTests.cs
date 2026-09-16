using HotspotGo.Cli;
using Xunit;

namespace HotspotGo.Tests.Cli;

/// <summary>
/// 命令行参数解析。
///
/// 这是本工具对外的唯一接口面 —— README 的命令表、用户的计划任务、脚本都直接依赖它,
/// 所以每个开关的语义都锁死在这里。
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
    }

    /// <summary>--wait 位于参数末尾(后面没有值)时不越界,保持默认值。</summary>
    [Fact]
    public void Parse_wait_without_following_value_keeps_default()
    {
        Assert.Equal(Options.DefaultMaxWaitSeconds, Options.Parse(new[] { "--wait" }).MaxWaitSeconds);
    }

    /// <summary>同一开关出现多次时,最后出现的生效。</summary>
    [Fact]
    public void Parse_last_occurrence_wins()
    {
        Assert.Equal(HotspotCommand.TurnOff, Options.Parse(new[] { "--status", "--off" }).Command);
        Assert.Equal(15, Options.Parse(new[] { "--wait", "60", "--wait", "15" }).MaxWaitSeconds);
    }

    /// <summary>未知参数静默忽略(启动日志会原样记录 args,便于排查手误)。</summary>
    [Fact]
    public void Parse_ignores_unknown_arguments()
    {
        var options = Options.Parse(new[] { "-x", "--unknown", "stray", "--wait", "15", "--status" });

        Assert.Equal(HotspotCommand.Status, options.Command);
        Assert.Equal(15, options.MaxWaitSeconds);
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
