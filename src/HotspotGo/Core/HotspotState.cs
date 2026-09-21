namespace HotspotGo.Core;

/// <summary>
/// 热点运行状态的取值。
///
/// "开没开"是 Core 自己的业务判断(决定跳过还是发指令),所以常量归 Core;
/// WinRT 侧反过来引用它,不再是 Core 去问 WinRT 要这个字面量。
/// 值必须与 <c>TetheringOperationalState</c> 枚举的文本一致
/// —— 由 <c>TetheringApiTests.State_on_matches_winrt_enum_text</c> 逐字锁死。
/// </summary>
internal static class HotspotState
{
    /// <summary>已开启。</summary>
    public const string On = "On";
}
