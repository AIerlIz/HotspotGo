namespace HotspotGo.Core;

/// <summary>
/// 热点运行状态的取值与"是否已达标"的判定。
///
/// "开没开"是 Core 自己的业务判断(决定继续等还是收工),所以常量与判定都归 Core;
/// WinRT 侧反过来引用它,不再是 Core 去问 WinRT 要这个字面量。
/// 值必须与 <c>TetheringOperationalState</c> 枚举的文本一致
/// —— 由 <c>TetheringApiTests.State_on_matches_winrt_enum_text</c> 逐字锁死。
/// </summary>
internal static class HotspotState
{
    /// <summary>已开启。</summary>
    public const string On = "On";

    /// <summary>
    /// 读到的状态是否已经达到目标。
    ///
    /// 开 → 必须正好是 <see cref="On"/>;
    /// 关 → 任何"确定不是 On"的状态都算达标(用"不再 On"而不是"等于 Off",
    ///      是为了避免 Unavailable 这类中间态导致一直等到超时),
    ///      但 <b>读不到不算</b>(<see cref="UnknownValue.Placeholder"/> 或 null):
    ///      那只是没读到,不是关成功 —— 以前这里用"不等于 On",读失败会被误报成"关闭成功"。
    ///
    /// 为什么放在 Core 而不是 WinRT 层:它决定"还要不要继续等",是业务判断;
    /// 而且只有放在这里才测得到 —— 真机开 / 关热点没法写单元测试
    /// (见 <c>TetheringApi.ToggleAndWait</c> 为何只能靠轮询状态判成败)。
    /// </summary>
    public static bool IsTargetReached(string state, bool start)
    {
        if (state == null || state == UnknownValue.Placeholder) return false;

        // 开 → 必须是 On;关 → 只要已经不是 On 就算达标(仍读到 On = 还没停下来,继续等)
        return start ? state == On : state != On;
    }
}
