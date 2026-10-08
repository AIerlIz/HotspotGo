using System;
using HotspotGo.Logging;
using Xunit;

namespace HotspotGo.Tests.Logging;

/// <summary>
/// 一份日志写两个去处(终端 + log.txt)。
///
/// 这里守的重点是"分开":终端那份出事不能连累文件那份 ——
/// 终端一关就什么都没了,log.txt 才是事发之后唯一的凭据。
/// </summary>
public class MirrorLoggerTests
{
    /// <summary>每一行都写到所有去处。</summary>
    [Fact]
    public void Writes_every_line_to_every_sink()
    {
        var first = new FakeLogger();
        var second = new FakeLogger();

        new MirrorLogger(first, second).WriteLine("一行日志");

        Assert.Contains("一行日志", first.Text);
        Assert.Contains("一行日志", second.Text);
    }

    /// <summary>
    /// 前一个去处抛异常时,后面的仍要拿到这一行 ——
    /// <see cref="ILogger"/> 的契约虽然写了"实现绝不抛出",但丢记录的代价不该靠赌。
    /// </summary>
    [Fact]
    public void Keeps_writing_to_the_rest_when_one_sink_throws()
    {
        var fileLog = new FakeLogger();

        var mirror = new MirrorLogger(new ThrowingLogger(), fileLog);

        Assert.Null(Record.Exception(() => mirror.WriteLine("要紧的一行")));
        Assert.Contains("要紧的一行", fileLog.Text);
    }

    /// <summary>没有任何去处时也不该抛(空列表这种边界不该崩在日志上)。</summary>
    [Fact]
    public void Tolerates_having_no_sink_at_all()
    {
        Assert.Null(Record.Exception(() => new MirrorLogger().WriteLine("没人接")));
    }

    /// <summary>一个"写入即抛"的日志器 —— 违反 ILogger 契约的那种实现。</summary>
    private sealed class ThrowingLogger : ILogger
    {
        public void WriteLine(string message) => throw new InvalidOperationException("模拟这个通道坏了");
    }
}
