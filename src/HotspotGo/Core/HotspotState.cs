namespace HotspotGo.Core;

/// <summary>
/// 热点运行状态的取值与"是否已达标"的判定。
///
/// "开没开"是 Core 自己的业务判断(决定继续等还是收工),所以常量与判定都归 Core;
/// WinRT 侧反过来引用它,不再是 Core 去问 WinRT 要这个字面量。
/// 值必须与 <c>TetheringOperationalState</c> 枚举的文本一致
/// —— 由 <c>HotspotStateTests</c> 逐字锁死。
/// </summary>
internal static class HotspotState
{
    /// <summary>已开启。</summary>
    public const string On = "On";

    /// <summary>
    /// 正在切换(开 ↔ 关的过程中),操作还没落定。
    ///
    /// 这必须单独认出来,不能混进"确定状态"里 —— 有一次真机操作把它留下了现场:
    /// 一次关闭让系统进入 InTransition 之后再没出来,而当时"状态 != On 就算达标"的判定
    /// 立刻报了"关闭结果: 成功(状态 On → InTransition, 耗时 0 毫秒)"。
    /// 两个后果都很难查:① 把"没停干净"说成了成功,看日志的人会以为已经关了;
    /// ② 之后的 <c>--off</c> 因为"状态不是 On"而拒绝再动手,于是卡死之后工具自己也救不回来。
    ///
    /// 值必须与枚举文本一致 —— 由 <c>HotspotStateTests</c> 逐字锁死。
    /// </summary>
    public const string InTransition = "InTransition";

    /// <summary>
    /// 读到的状态是否已经达到目标。
    ///
    /// 开 → 必须正好是 <see cref="On"/>;
    /// 关 → 任何"确定不是 On"的状态都算达标(用"不再 On"而不是"等于 Off",
    ///      是为了不让 Unavailable 这类取值把轮询拖到超时),
    ///      但两种情况都不算达标:
    ///      <list type="bullet">
    ///        <item><b>读不到</b>(<see cref="UnknownValue.Placeholder"/> 或 null)——
    ///          那只是没读到,不是关成功;</item>
    ///        <item><b>还在切换中</b>(<see cref="InTransition"/>)—— 没落定就是没达标。
    ///          宁可等到超时报一句实话,也不要早报一句成功。</item>
    ///      </list>
    ///
    /// 为什么放在 Core 而不是 WinRT 层:它决定"还要不要继续等",是业务判断;
    /// 而且只有放在这里才测得到 —— 真机开 / 关热点没法写单元测试
    /// (见 <c>TetheringApi.ToggleAndWait</c> 为何只能靠轮询状态判成败)。
    /// </summary>
    public static bool IsTargetReached(string state, bool start)
    {
        if (state == null || state == UnknownValue.Placeholder) return false;

        if (state == InTransition) return false;

        // 开 → 必须是 On;关 → 只要已经不是 On 就算达标(仍读到 On = 还没停下来,继续等)
        return start ? state == On : state != On;
    }
}
