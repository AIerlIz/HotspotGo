using System;

namespace HotspotGo.Tests.WinRT;

/// <summary>
/// 供反射通道测试使用的假类型 —— 形状照着 WinRT 投影出来的成员来:
/// 有重载的静态方法、属性、无参方法、只有 Async 后缀的成员。
///
/// 必须是 public:反射查找用的是 Public 绑定标志,非 public 成员找不到。
/// </summary>
public static class FakeStaticApi
{
    /// <summary>三个重载,用来验证反射挑重载时不会抛 AmbiguousMatchException。</summary>
    public static string Describe(string value) => "string:" + value;

    public static string Describe(int value) => "int:" + value;

    public static string Describe() => "none";
}
