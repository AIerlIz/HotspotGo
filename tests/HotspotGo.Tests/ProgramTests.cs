using System;
using HotspotGo.Cli;
using Xunit;

namespace HotspotGo.Tests;

/// <summary>
/// 入口的"启动 / 结束 / 顶层异常兜底",以及参数层面的分流。
///
/// 本程序是 WinExe,出问题时用户只能看 log.txt,所以"每次运行都留下首尾两行、
/// 致命异常原样留档"是硬约定(冒烟脚本也靠首尾两行判定日志完整)。
/// 这里把这条约定钉死 —— 它是唯一保证"程序炸了还留得下线索"的地方。
///
/// <see cref="Program.Preflight"/> 那一组守的是另一条硬约定:本工具不带参数就是
/// "开热点",所以无法识别的参数必须被拦住、不能回落成默认动作 ——
/// 否则 <c>--off</c> 敲错一个字会把热点开起来,方向正好相反。
///
/// 注意这里测的是 <see cref="Program.Execute"/> / <see cref="Program.Preflight"/>
/// 这段框架逻辑,不是真实开热点;真实链路仍由冒烟脚本跑 exe 覆盖。
/// </summary>
public class ProgramTests
{
    /// <summary>正常路径:退出码原样透传,首尾两行都写出来。</summary>
    [Fact]
    public void Returns_body_exit_code_and_writes_start_and_end()
    {
        var log = new FakeLogger();

        var code = Program.Execute(log, new[] { "--status" }, () => (int)ExitCode.Ok);

        Assert.Equal((int)ExitCode.Ok, code);
        Assert.Contains("===== 启动 (args: --status) =====", log.Text);
        Assert.Contains("===== 结束 =====", log.Text);
    }

    /// <summary>退出码是给脚本 / 计划任务看的,入口不许改写它(2 / 3 / 4 都要原样透传)。</summary>
    [Fact]
    public void Passes_body_exit_code_through()
    {
        var log = new FakeLogger();

        Assert.Equal((int)ExitCode.NoUpstream, Program.Execute(log, new string[0], () => (int)ExitCode.NoUpstream));
        Assert.Equal((int)ExitCode.StartFailed, Program.Execute(log, new string[0], () => (int)ExitCode.StartFailed));
    }

    /// <summary>命令行参数原样回显 —— 排查"是不是参数敲错了"全靠这一行。</summary>
    [Fact]
    public void Echoes_command_line_arguments()
    {
        var log = new FakeLogger();

        Program.Execute(log, new[] { "--wait", "5", "--off" }, () => (int)ExitCode.Ok);

        Assert.Contains("args: --wait 5 --off", log.Text);
    }

    /// <summary>主体抛异常 → 退出码 1(Fatal),异常原文进日志,不往上冒。</summary>
    [Fact]
    public void Returns_Fatal_and_logs_exception_when_body_throws()
    {
        var log = new FakeLogger();

        var code = Program.Execute(log, new string[0],
            () => throw new InvalidOperationException("模拟顶层异常"));

        Assert.Equal((int)ExitCode.Fatal, code);
        Assert.Contains("[FATAL]", log.Text);
        Assert.Contains("模拟顶层异常", log.Text);
    }

    /// <summary>致命异常时"结束"行仍要写出(finally 的作用),否则日志看不出这次跑完了没。</summary>
    [Fact]
    public void Writes_end_marker_even_when_body_throws()
    {
        var log = new FakeLogger();

        Program.Execute(log, new string[0], () => throw new InvalidOperationException("模拟顶层异常"));

        Assert.Contains("===== 结束 =====", log.Text);
    }

    // ===================================================================
    // 参数层分流:--help / 无法识别的参数
    // ===================================================================

    /// <summary>
    /// --help:写用法说明,退出码 0(这是一次成功的请求,不是错误)。
    /// 说明必须落 log.txt —— 无控制台,那是唯一出口。
    /// </summary>
    [Fact]
    public void Preflight_writes_usage_and_succeeds_for_help()
    {
        var log = new FakeLogger();

        ExitCode? code = Program.Preflight(log, Options.Parse(new[] { "--help" }));

        Assert.True(code.HasValue);
        Assert.Equal(ExitCode.Ok, code.Value);
        Assert.Contains("用法:HotspotGo.exe", log.Text);
        Assert.Contains("--off", log.Text);
    }

    /// <summary>
    /// --version / -v:写版本信息,退出码 0。
    /// 与 --help 一样是"问一句就走",不碰任何机器状态。
    /// </summary>
    [Fact]
    public void Preflight_writes_version_and_succeeds()
    {
        var log = new FakeLogger();

        ExitCode? code = Program.Preflight(log, Options.Parse(new[] { "-v" }));

        Assert.True(code.HasValue);
        Assert.Equal(ExitCode.Ok, code.Value);
        Assert.Contains("热点快启 HotspotGo", log.Text);
    }

    /// <summary>两个都要时以说明为准 —— 说明里本来就写着版本怎么看。</summary>
    [Fact]
    public void Preflight_prefers_help_over_version()
    {
        var log = new FakeLogger();

        ExitCode? code = Program.Preflight(log, Options.Parse(new[] { "-v", "-h" }));

        Assert.True(code.HasValue);
        Assert.Equal(ExitCode.Ok, code.Value);
        Assert.Contains("用法:HotspotGo.exe", log.Text);
        Assert.DoesNotContain("热点快启 HotspotGo", log.Text);
    }

    /// <summary>版本请求与无法识别的参数同时出现时,先出结果、不按报错处理(同 --help 的取舍)。</summary>
    [Fact]
    public void Preflight_prefers_version_over_unknown_arguments()
    {
        var log = new FakeLogger();

        ExitCode? code = Program.Preflight(log, Options.Parse(new[] { "--pff", "-v" }));

        Assert.True(code.HasValue);
        Assert.Equal(ExitCode.Ok, code.Value);
        Assert.Contains("热点快启 HotspotGo", log.Text);
    }

    /// <summary>
    /// 无法识别的参数:一律不执行任何操作(退出码 5),并把参数名与说明都记下来。
    /// 这是"手误不该反向执行"的最后一道闸 —— 落到业务层就晚了。
    /// </summary>
    [Fact]
    public void Preflight_rejects_unknown_arguments()
    {
        var log = new FakeLogger();

        ExitCode? code = Program.Preflight(log, Options.Parse(new[] { "--pff" }));

        Assert.True(code.HasValue);
        Assert.Equal(ExitCode.BadArguments, code.Value);
        Assert.Contains("--pff", log.Text);
        Assert.Contains("本次不做任何操作", log.Text);
    }

    /// <summary>参数正常时不做任何输出、也不决定退出码,纯粹放行给业务服务。</summary>
    [Fact]
    public void Preflight_lets_valid_options_through_silently()
    {
        var log = new FakeLogger();

        Assert.Null(Program.Preflight(log, Options.Parse(new[] { "--status" })));
        Assert.Equal(string.Empty, log.Text);
    }

    /// <summary>用户明确要看说明时,说明比报错有用:--help 优先于未知参数。</summary>
    [Fact]
    public void Preflight_prefers_help_over_unknown_arguments()
    {
        var log = new FakeLogger();

        ExitCode? code = Program.Preflight(log, Options.Parse(new[] { "--pff", "--help" }));

        Assert.True(code.HasValue);
        Assert.Equal(ExitCode.Ok, code.Value);
    }
}
