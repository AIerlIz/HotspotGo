using System;

namespace HotspotGo.Tests.WinRT;

/// <summary>
/// 模拟 WinRT 热点管理器的成员形状,用来验证反射通道的各级回退策略。
/// </summary>
public class FakeManager
{
    /// <summary>普通公共属性 —— 最直接的一条路。</summary>
    public string TetheringOperationalState { get; set; } = "Off";

    /// <summary>公共无参方法 —— 反射通道优先找方法,再找属性。</summary>
    public string MaxClientCount() => "8";

    /// <summary>只有不带 Async 后缀的成员 —— 验证去掉 Async 后能回退命中。</summary>
    public string GetSsid() => "MY-SSID";

    /// <summary>只有大小写不同的成员 —— 验证大小写不敏感的那级回退。</summary>
    public string ssidLower() => "case-insensitive";

    /// <summary>取值就抛异常的属性 —— 验证诊断路径会吞掉异常而不是打断整轮探测。</summary>
    public string ThrowingProperty => throw new InvalidOperationException("boom");
}
