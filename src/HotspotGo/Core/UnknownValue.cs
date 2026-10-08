namespace HotspotGo.Core;

/// <summary>
/// "这个值没读到"的统一占位文本。
///
/// WinRT 层读不到属性时返回它(<c>WinrtReflection.GetPropertyString</c>),
/// Core 侧据此把"读失败"和"真的读到了一个值"分开 ——
/// 例如关热点时如果把 <c>(null)</c> 当成"已经不是 On",就会把一次读失败误报成"关闭成功"。
///
/// WinRT 层不认识本类型(那是更底层的通用工具,不该依赖 Core 的常量),
/// 两边的字面量由 <c>WinrtReflectionTests.Placeholder_matches_the_core_contract</c> 锁死。
/// </summary>
internal static class UnknownValue
{
    /// <summary>读不到值时的占位文本。</summary>
    public const string Placeholder = "(null)";
}
