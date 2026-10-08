using HotspotGo.Core;
using HotspotGo.WinRT;
using Xunit;

namespace HotspotGo.Tests.WinRT;

/// <summary>
/// 连接查询封装里不依赖系统状态的纯逻辑:类型限定名与空目标兜底。
/// </summary>
public class ConnectivityApiTests
{
    /// <summary>
    /// 同上(TetheringApiTests):限定名必须逐字符正确,否则类型解析不到,
    /// "当前上网连接"这条链路整个断掉。
    /// </summary>
    [Fact]
    public void Type_name_matches_winrt_qualified_format()
    {
        Assert.Equal(
            "Windows.Networking.Connectivity.NetworkInformation, Windows.Networking, ContentType=WindowsRuntime",
            ConnectivityApi.TypeName);
    }

    /// <summary>TypeName 由 TypeFullName 加后缀组成。</summary>
    [Fact]
    public void Type_name_is_built_from_full_name()
    {
        Assert.StartsWith(ConnectivityApi.TypeFullName, ConnectivityApi.TypeName);
    }

    /// <summary>读不到连接名 / 连接等级时返回 "(null)",不抛异常。</summary>
    [Fact]
    public void Read_members_return_placeholder_for_null_profile()
    {
        Assert.Equal("(null)", ConnectivityApi.ReadProfileName(null));
        Assert.Equal("(null)", ConnectivityApi.ReadConnectivityLevel(null));
    }

    // ===================================================================
    // 等级字面量(跨层契约)
    // ===================================================================

    /// <summary>
    /// 判定"这条连接能不能开热点"用的字面量必须与 WinRT 枚举文本一致
    /// (<c>NetworkConnectivityLevel.None</c> / <c>.InternetAccess</c>)。
    ///
    /// 常量归 Core(<see cref="ConnectivityLevel"/>),这里守的是跨层契约 ——
    /// 对不上就会把断开的网卡当成可用,或者永远挑不到能上外网的那条。
    /// </summary>
    [Fact]
    public void Level_values_match_winrt_enum_text()
    {
        Assert.Equal("None", ConnectivityLevel.None);
        Assert.Equal("LocalAccess", ConnectivityLevel.LocalAccess);
        Assert.Equal("InternetAccess", ConnectivityLevel.InternetAccess);
    }

    /// <summary>
    /// "读不到"的占位文本两端必须一致:Core 靠比对它来区分"读失败"和"真的读到了值"
    /// (见 <c>HotspotState.IsTargetReached</c>),不一致的话关热点时会把读失败当成成功。
    /// </summary>
    [Fact]
    public void Placeholder_matches_the_core_contract()
    {
        Assert.Equal(UnknownValue.Placeholder, ConnectivityApi.ReadConnectivityLevel(null));
    }
}
