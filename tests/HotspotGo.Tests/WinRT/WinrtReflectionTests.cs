using System;
using HotspotGo.WinRT;
using Xunit;

namespace HotspotGo.Tests.WinRT;

/// <summary>
/// WinRT 反射通道。
///
/// 这是本项目能不依赖 24 MB 投影程序集的关键,也是唯一"手写"的底层机制:
/// 类型解析、重载挑选、成员调用的各级回退全在这里。它一旦出错,
/// 表现是热点功能整体不可用,所以每个回退分支都要有测试兜着。
///
/// 这里全部用假类型来测,不去碰真实 WinRT —— 单元测试要能在任何 Windows 上稳定复现。
/// </summary>
public class WinrtReflectionTests
{
    // ===================================================================
    // 类型解析
    // ===================================================================

    /// <summary>null 或解析不到的类型名一律返回 null,不抛异常。</summary>
    [Fact]
    public void FindType_returns_null_for_unresolvable_names()
    {
        Assert.Null(WinrtReflection.FindType(null));
        Assert.Null(WinrtReflection.FindType("No.Such.Type, No.Such.Assembly"));
    }

    /// <summary>查不到的结果也会被缓存 —— 重复查询返回同一结果(避免反复尝试解析)。</summary>
    [Fact]
    public void FindType_caches_negative_results()
    {
        const string missing = "No.Such.Type.At.All, No.Such.Assembly";

        Assert.Null(WinrtReflection.FindType(missing));
        Assert.Null(WinrtReflection.FindType(missing));
    }

    /// <summary>能解析的类型返回同一个 Type 实例(走的是缓存)。</summary>
    [Fact]
    public void FindType_caches_resolved_types()
    {
        var name = typeof(FakeStaticApi).AssemblyQualifiedName;

        var first = WinrtReflection.FindType(name);
        var second = WinrtReflection.FindType(name);

        Assert.NotNull(first);
        Assert.Same(first, second);
    }

    // ===================================================================
    // 静态方法调用
    // ===================================================================

    /// <summary>
    /// 有重载时必须按参数类型挑对那一个,而不是抛 AmbiguousMatchException
    /// —— WinRT 类型普遍有重载,直接用 GetMethod(名, flags) 会当场炸掉。
    /// </summary>
    [Theory]
    [InlineData("s", "string:s")]
    [InlineData(7, "int:7")]
    public void InvokeStatic_resolves_overload_by_argument_type(object arg, string expected)
    {
        Assert.Equal(expected, WinrtReflection.InvokeStatic(typeof(FakeStaticApi), "Describe", new[] { arg }));
    }

    /// <summary>零参数调用走无参重载。</summary>
    [Fact]
    public void InvokeStatic_calls_parameterless_overload()
    {
        Assert.Equal("none", WinrtReflection.InvokeStatic(typeof(FakeStaticApi), "Describe"));
    }

    /// <summary>方法不存在时抛 MissingMethodException(错误约定:类型/成员不可用就抛,由调用方记录)。</summary>
    [Fact]
    public void InvokeStatic_throws_when_method_missing()
    {
        Assert.Throws<MissingMethodException>(
            () => WinrtReflection.InvokeStatic(typeof(FakeStaticApi), "NoSuchMethod"));
    }

    // ===================================================================
    // 实例成员的各级回退
    // ===================================================================

    /// <summary>第一级:直接命中同名公共无参方法。</summary>
    [Fact]
    public void SafeInvoke_hits_plain_method()
    {
        Assert.Equal("8", WinrtReflection.SafeInvoke(new FakeManager(), "MaxClientCount"));
    }

    /// <summary>第二级:没有方法时读同名公共属性。</summary>
    [Fact]
    public void SafeInvoke_falls_back_to_property()
    {
        Assert.Equal("On", WinrtReflection.SafeInvoke(
            new FakeManager { TetheringOperationalState = "On" }, "TetheringOperationalState"));
    }

    /// <summary>第三级:带 Async 后缀找不到时,去掉后缀再找一次(WinRT 投影差异)。</summary>
    [Fact]
    public void SafeInvoke_strips_async_suffix()
    {
        Assert.Equal("MY-SSID", WinrtReflection.SafeInvoke(new FakeManager(), "GetSsidAsync"));
    }

    /// <summary>第四级:大小写不敏感兜底。</summary>
    [Fact]
    public void SafeInvoke_falls_back_to_case_insensitive_match()
    {
        Assert.Equal("case-insensitive", WinrtReflection.SafeInvoke(new FakeManager(), "SSIDLOWER"));
    }

    /// <summary>成员不存在返回 null,不抛异常 —— 诊断路径不能打断整轮探测。</summary>
    [Fact]
    public void SafeInvoke_returns_null_for_missing_member()
    {
        Assert.Null(WinrtReflection.SafeInvoke(new FakeManager(), "NoSuchMember"));
    }

    /// <summary>目标为 null 时返回 null,不抛 NullReferenceException。</summary>
    [Fact]
    public void SafeInvoke_returns_null_for_null_target()
    {
        Assert.Null(WinrtReflection.SafeInvoke(null, "Anything"));
    }

    /// <summary>取值过程抛出的异常被吞掉,返回 null。</summary>
    [Fact]
    public void SafeInvoke_swallows_member_exceptions()
    {
        Assert.Null(WinrtReflection.SafeInvoke(new FakeManager(), "ThrowingProperty"));
    }

    // ===================================================================
    // 字符串读取
    // ===================================================================

    /// <summary>正常取值时返回其字符串形式。</summary>
    [Fact]
    public void GetPropertyString_reads_value()
    {
        Assert.Equal("Off", WinrtReflection.GetPropertyString(new FakeManager(), "TetheringOperationalState"));
    }

    /// <summary>属性不存在时显示 "(null)" —— 日志里就靠这个字符串区分"读不到"和"值是空"。</summary>
    [Fact]
    public void GetPropertyString_returns_placeholder_when_property_missing()
    {
        Assert.Equal("(null)", WinrtReflection.GetPropertyString(new FakeManager(), "NoSuchProperty"));
    }

    /// <summary>目标为 null 时同样显示 "(null)"。</summary>
    [Fact]
    public void GetPropertyString_returns_placeholder_when_target_is_null()
    {
        Assert.Equal("(null)", WinrtReflection.GetPropertyString(null, "TetheringOperationalState"));
    }
}
