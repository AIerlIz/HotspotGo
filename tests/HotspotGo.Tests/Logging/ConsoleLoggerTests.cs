using System;
using System.IO;
using System.Text;
using HotspotGo.Logging;
using Xunit;

namespace HotspotGo.Tests.Logging;

/// <summary>
/// 写终端那份日志。
///
/// 与 <see cref="AppendFileLoggerTests"/> 同样的重点:它也必须"绝不抛出"。
/// 这条不是洁癖 —— 输出被下游接走时随时可能断
/// (<c>HotspotGo.exe --status | head</c>:下游看够了就关管道,下一刻写入就炸),
/// 而那一刻正在做的可能是"开热点"。
/// </summary>
public class ConsoleLoggerTests
{
    /// <summary>正常情况:整行写到指定输出。</summary>
    [Fact]
    public void Writes_the_line_to_the_given_output()
    {
        var output = new StringWriter();

        new ConsoleLogger(output).WriteLine("一条日志");

        Assert.Contains("一条日志", output.ToString());
    }

    /// <summary>下游把管道关掉时,写入失败必须静默 —— 不能把主流程带崩。</summary>
    [Fact]
    public void Never_throws_when_the_output_is_broken()
    {
        var logger = new ConsoleLogger(new BrokenWriter());

        Assert.Null(Record.Exception(() => logger.WriteLine("不该抛出")));
    }

    /// <summary>一个"写入即抛"的输出 —— 模拟下游提前关掉管道。</summary>
    private sealed class BrokenWriter : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;

        public override void WriteLine(string value) => throw new IOException("模拟管道被下游关掉");

        public override void Flush() => throw new IOException("模拟管道被下游关掉");
    }
}
