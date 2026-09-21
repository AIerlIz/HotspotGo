using System.Collections.Generic;
using HotspotGo.Logging;

namespace HotspotGo.Tests;

/// <summary>
/// 内存日志器:把写过的每一行留在内存里,便于断言"这条路径确实走了,而且文案是对的"。
///
/// 必须是 internal —— <see cref="ILogger"/> 本身是 internal,
/// public 类实现它会因可访问性不一致编译不过。
/// </summary>
internal sealed class FakeLogger : ILogger
{
    private readonly List<string> _lines = new List<string>();

    /// <summary>整份日志拼成一段文本,断言关键词时用。</summary>
    public string Text => string.Join("\n", _lines);

    public void WriteLine(string message) => _lines.Add(message);
}
