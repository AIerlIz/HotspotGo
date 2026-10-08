using System;
using System.IO;

namespace HotspotGo.Logging;

/// <summary>
/// 把日志同时写到标准输出 —— "给正在看的人"的那一份。
///
/// 与 <see cref="AppendFileLogger"/> 一样是"绝不抛出"的实现(契约见 <see cref="ILogger"/>):
/// 接走输出的那一头随时可能断掉(<c>HotspotGo.exe --status | head</c> 这类),断掉时
/// 只该少一行输出,不该让"开热点"这件事失败。
/// </summary>
internal sealed class ConsoleLogger : ILogger
{
    private readonly TextWriter _output;

    /// <summary>写到真正的标准输出。</summary>
    public ConsoleLogger() : this(Console.Out) { }

    /// <summary>写到指定输出 —— 抽出来是为了能测"那头断了会怎样"。</summary>
    public ConsoleLogger(TextWriter output)
    {
        _output = output;
    }

    public void WriteLine(string message)
    {
        try
        {
            _output.WriteLine(message);
            _output.Flush();
        }
        catch
        {
            /* 静默 — 终端写不出去不该中断主流程 */
        }
    }
}
