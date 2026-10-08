using System;

namespace HotspotGo.Logging;

/// <summary>
/// 把同一行写给多个日志器 —— 终端与 log.txt 各一份。
///
/// 为什么两份都要:log.txt 是事发之后唯一还在的凭据(终端一关就没了),
/// 终端那份是给正在敲命令的人看的。
///
/// 为什么每个 sink 都要单独 try:<see cref="ILogger"/> 的契约本来就写了"实现绝不抛出",
/// 但这里不能靠赌 —— 终端那一份出事的代价,不该是丢掉 log.txt 里那条记录。
/// 顺序上也把文件那份放前面,让"要紧的那份"先落地。
/// </summary>
internal sealed class MirrorLogger : ILogger
{
    private readonly ILogger[] _sinks;

    public MirrorLogger(params ILogger[] sinks)
    {
        _sinks = sinks;
    }

    public void WriteLine(string message)
    {
        foreach (var sink in _sinks)
        {
            try
            {
                sink.WriteLine(message);
            }
            catch
            {
                /* 静默 — 一个通道坏了,不该影响其余通道 */
            }
        }
    }
}
