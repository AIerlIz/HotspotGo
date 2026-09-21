using System;
using Xunit;

namespace HotspotGo.Tests;

/// <summary>
/// 入口的"启动 / 结束 / 顶层异常兜底"。
///
/// 本程序是 WinExe,出问题时用户只能看 log.txt,所以"每次运行都留下首尾两行、
/// 致命异常原样留档"是硬约定(冒烟脚本也靠首尾两行判定日志完整)。
/// 这里把这条约定钉死 —— 它是唯一保证"程序炸了还留得下线索"的地方。
///
/// 注意这里测的是 <see cref="Program.Execute"/> 这段框架逻辑,不是真实开热点;
/// 真实链路仍由冒烟脚本跑 exe 覆盖。
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
}
