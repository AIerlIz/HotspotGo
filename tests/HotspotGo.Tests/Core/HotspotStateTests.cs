using HotspotGo.Core;
using Xunit;

namespace HotspotGo.Tests.Core;

/// <summary>
/// "状态达到目标了没有"的判定。
///
/// 这段判断决定轮询什么时候停、以及最后报成功还是失败,而真机开 / 关热点
/// 是没法写单元测试的 —— 所以它特意被放在 Core(以前在 WinRT 层的
/// <c>TetheringApi</c> 里,那里测不到),由这里逐条钉死。
///
/// 两条"不算达标"是拿真机事故换来的:
///   - <b>读不到状态</b>不算:旧判定是"不等于 On",于是读失败会被当成"关成功";
///   - <b>切换中</b>不算:一次关闭把系统带进 InTransition 后就再没出来,
///     而"不等于 On 就算达标"当场报了"关闭成功(状态 On → InTransition, 耗时 0 毫秒)"。
/// </summary>
public class HotspotStateTests
{
    /// <summary>开:只有正好是 On 才算达标。</summary>
    [Theory]
    [InlineData("On", true)]
    [InlineData("Off", false)]
    [InlineData("Starting", false)]
    [InlineData("InTransition", false)]
    [InlineData("Unavailable", false)]
    [InlineData(UnknownValue.Placeholder, false)]
    public void Start_is_reached_only_when_the_state_is_on(string state, bool expected)
    {
        Assert.Equal(expected, HotspotState.IsTargetReached(state, start: true));
    }

    /// <summary>
    /// 关:任何"确定不是 On"的状态都算达标 ——
    /// 用"不再 On"而不是"等于 Off",是为了避开 Unavailable 这类取值把轮询拖到超时。
    /// 但 InTransition 不算(见下面那条独立用例)。
    /// </summary>
    [Theory]
    [InlineData("Off", true)]
    [InlineData("Unavailable", true)]
    [InlineData("On", false)]
    [InlineData("InTransition", false)]
    [InlineData(UnknownValue.Placeholder, false)]
    public void Stop_is_reached_for_any_settled_state_other_than_on(string state, bool expected)
    {
        Assert.Equal(expected, HotspotState.IsTargetReached(state, start: false));
    }

    /// <summary>
    /// 切换中(InTransition)不算达标 —— 这条是拿真机事故换来的。
    ///
    /// 现场:一次 <c>--off</c> 让系统进入 InTransition 之后一直没出来,而当时
    /// "状态 != On 就算达标"的判定立刻报了"关闭结果: 成功(状态 On → InTransition, 耗时 0 毫秒)"。
    /// 于是 ① 看日志的人以为已经关了;② 之后再跑 <c>--off</c> 因为"状态不是 On"而拒绝动手,
    /// 卡死之后工具自己也救不回来。宁可等到超时报一句实话,也不要早报一句成功。
    /// </summary>
    [Fact]
    public void Transitioning_is_not_reached_in_either_direction()
    {
        Assert.False(HotspotState.IsTargetReached(HotspotState.InTransition, start: false));
        Assert.False(HotspotState.IsTargetReached(HotspotState.InTransition, start: true));
    }

    /// <summary>null(端口实现直接返回 null)同样算"读不到",两个方向都不算达标。</summary>
    [Fact]
    public void Unreadable_state_never_counts_as_reached()
    {
        Assert.False(HotspotState.IsTargetReached(null, start: true));
        Assert.False(HotspotState.IsTargetReached(null, start: false));
    }

    /// <summary>"已开启"的取值就是 WinRT 枚举文本,这里再钉一次(另有 TetheringApiTests 守着)。</summary>
    [Fact]
    public void On_matches_the_winrt_enum_text()
    {
        Assert.Equal("On", HotspotState.On);
    }

    /// <summary>
    /// "切换中"的取值同样必须与 WinRT 枚举文本逐字一致 ——
    /// 拼错就等于根本没把这状态识别出来,上面那条豁免立刻失效。
    /// </summary>
    [Fact]
    public void InTransition_matches_the_winrt_enum_text()
    {
        Assert.Equal("InTransition", HotspotState.InTransition);
    }
}
