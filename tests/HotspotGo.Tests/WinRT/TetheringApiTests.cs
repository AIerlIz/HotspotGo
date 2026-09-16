using System;
using HotspotGo.WinRT;
using Xunit;

namespace HotspotGo.Tests.WinRT;

/// <summary>
/// 热点管理器反射封装里不依赖系统状态的纯逻辑:
/// 类型限定名的拼法、空目标的兜底、开/关结果对象。
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

    /// <summary>"已开启"的判定值必须与 WinRT 枚举文本一致(TetheringOperationalState.On)。</summary>
    [Fact]
    public void State_on_matches_winrt_enum_text()
    {
        Assert.Equal("On", TetheringApi.StateOn);
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

    // ===================================================================
    // 开关结果对象
    // ===================================================================

    /// <summary>成功结果:状态达标、记下前后状态与耗时,没有错误信息。</summary>
    [Fact]
    public void ToggleResult_success_carries_state_transition()
    {
        var result = TetheringApi.ToggleResult.Success("Off", "On", TimeSpan.FromMilliseconds(1234));

        Assert.True(result.Succeeded);
        Assert.Equal("Off", result.StateBefore);
        Assert.Equal("On", result.StateAfter);
        Assert.Equal(TimeSpan.FromMilliseconds(1234), result.Elapsed);
        Assert.Null(result.Error);
    }

    /// <summary>成功时的可读文本要带上状态迁移和耗时(写进 log.txt 的那一行)。</summary>
    [Fact]
    public void ToggleResult_success_describes_transition()
    {
        var describe = TetheringApi.ToggleResult.Success("Off", "On", TimeSpan.FromMilliseconds(1234)).Describe();

        Assert.Contains("成功", describe);
        Assert.Contains("Off → On", describe);
        Assert.Contains("1234", describe);
    }

    /// <summary>失败结果:没有耗时,带着原因。</summary>
    [Fact]
    public void ToggleResult_failure_carries_reason()
    {
        var result = TetheringApi.ToggleResult.Failure("Off", "等待超时");

        Assert.False(result.Succeeded);
        Assert.Equal("Off", result.StateAfter);
        Assert.Equal("等待超时", result.Error);
        Assert.Null(result.Elapsed);
    }

    /// <summary>失败时的可读文本要带上原因。</summary>
    [Fact]
    public void ToggleResult_failure_describes_reason()
    {
        var describe = TetheringApi.ToggleResult.Failure("Off", "等待超时").Describe();

        Assert.Contains("失败", describe);
        Assert.Contains("等待超时", describe);
    }
}
