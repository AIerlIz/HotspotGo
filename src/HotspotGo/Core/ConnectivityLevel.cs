namespace HotspotGo.Core;

/// <summary>
/// 连接等级的取值(Windows <c>NetworkConnectivityLevel</c> 枚举的文本形式)。
///
/// 与 <see cref="HotspotState"/> 同样的道理:"哪条连接能拿来开热点"是 Core 的业务判断
/// (见 <see cref="HotspotService"/> 里挑选共享源的那段),所以判定用的字面量归 Core,
/// WinRT 侧反过来引用它,不再是 Core 去问 WinRT 要这个字面量。
///
/// 值必须与枚举文本一致 —— 由 <c>ConnectivityApiTests</c> 逐字锁死。
/// </summary>
internal static class ConnectivityLevel
{
    /// <summary>完全未连接,不能作为共享源。</summary>
    public const string None = "None";

    /// <summary>
    /// 只连到本地网络,出不了外网。
    /// 主机没网时 <c>--any</c> 退而用它当共享源:连着热点的设备之间能互访,但上不了网。
    /// </summary>
    public const string LocalAccess = "LocalAccess";

    /// <summary>能出外网 —— 首选共享源。</summary>
    public const string InternetAccess = "InternetAccess";
}
