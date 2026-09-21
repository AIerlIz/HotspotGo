using HotspotGo.Core;
using HotspotGo.WinRT;
using Xunit;

namespace HotspotGo.Tests.WinRT;

/// <summary>
/// 热点管理器反射封装里不依赖系统状态的纯逻辑:类型限定名的拼法、空目标的兜底。
///
/// 真正调 WinRT 的那几条(创建管理器、读写状态、开/关)必须真机跑,
/// 由 CI 的冒烟步骤覆盖。
/// </summary>
public class TetheringApiTests
{
    // ===================================================================
    // 类型限定名
    // ===================================================================

    /// <summary>
    /// 限定名必须是 CLR 内建 WinRT 互操作唯一认得的形状:
    /// <c>"类型全名, WinRT 程序集名, ContentType=WindowsRuntime"</c>。
    ///
    /// 少一个逗号、程序集名写错、ContentType 拼错,整个热点功能都会失效,
    /// 而且只在运行时才暴露 —— 所以这里逐字锁死。
    /// </summary>
    [Fact]
    public void Type_name_matches_winrt_qualified_format()
    {
        Assert.Equal(
            "Windows.Networking.NetworkOperators.NetworkOperatorTetheringManager, Windows.Networking, ContentType=WindowsRuntime",
            TetheringApi.TypeName);
    }

    /// <summary>TypeName 由 TypeFullName 加后缀组成,两者不能各写一份而跑偏。</summary>
    [Fact]
    public void Type_name_is_built_from_full_name()
    {
        Assert.StartsWith(TetheringApi.TypeFullName, TetheringApi.TypeName);
    }

    /// <summary>
    /// "已开启"的判定值必须与 WinRT 枚举文本一致(TetheringOperationalState.On)。
    /// 常量归 Core(<see cref="HotspotState"/>),这里守的是跨层契约 —— 对不上就永远判定不了达标。
    /// </summary>
    [Fact]
    public void State_on_matches_winrt_enum_text()
    {
        Assert.Equal("On", HotspotState.On);
    }

    // ===================================================================
    // 空目标兜底
    // ===================================================================

    /// <summary>profile 为 null 时直接返回 null —— 这是"该上游不能作为共享源"的业务语义,不是错误。</summary>
    [Fact]
    public void CreateManager_returns_null_for_null_profile()
    {
        Assert.Null(TetheringApi.CreateManager(null));
    }

    /// <summary>读不到状态 / SSID / 客户端数时一律返回 "(null)",不抛异常。</summary>
    [Fact]
    public void Read_members_return_placeholder_for_null_manager()
    {
        Assert.Equal("(null)", TetheringApi.ReadState(null));
        Assert.Equal("(null)", TetheringApi.ReadSsid(null));
        Assert.Equal("(null)", TetheringApi.ReadClientCount(null));
        Assert.Equal("(null)", TetheringApi.ReadMaxClientCount(null));
    }
}
