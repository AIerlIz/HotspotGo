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
}
