namespace HotspotGo.Logging;

/// <summary>
/// 极简日志抽象:业务组件只依赖这一行接口,不绑定具体落盘方式。
/// 实现方必须保证 <see cref="WriteLine"/> 绝不抛出(见 <see cref="AppendFileLogger"/>)。
/// </summary>
internal interface ILogger
{
    /// <summary>追加一行日志。</summary>
    void WriteLine(string message);
}
