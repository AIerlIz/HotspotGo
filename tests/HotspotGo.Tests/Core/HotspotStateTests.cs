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
/// 关键的一条:"读不到状态"不算达标。以前关闭那条路用的是"不等于 On",
/// 于是读失败(<c>(null)</c>)会被当成"关成功",日志里出现"成功(状态 On → (null))"。
/// </summary>
public class HotspotStateTests
{
    /// <summary>开:只有正好是 On 才算达标。</summary>
    [Theory]
    [InlineData("On", true)]
    [InlineData("Off", false)]
    [InlineData("Starting", false)]
    [InlineData("Unavailable", false)]
    [InlineData(UnknownValue.Placeholder, false)]
    public void Start_is_reached_only_when_the_state_is_on(string state, bool expected)
    {
        Assert.Equal(expected, HotspotState.IsTargetReached(state, start: true));
    }

    /// <summary>
    /// 关:任何"确定不是 On"的状态都算达标 ——
    /// 用"不再 On"而不是"等于 Off",是为了避开 Unavailable 这类中间态一直等到超时。
    /// </summary>
    [Theory]
    [InlineData("Off", true)]
    [InlineData("Unavailable", true)]
    [InlineData("On", false)]
    [InlineData(UnknownValue.Placeholder, false)]
    public void Stop_is_reached_for_any_known_state_other_than_on(string state, bool expected)
    {
        Assert.Equal(expected, HotspotState.IsTargetReached(state, start: false));
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
}
