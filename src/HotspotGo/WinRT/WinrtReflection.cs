using System;
using System.Collections.Generic;
using System.Reflection;

namespace HotspotGo.WinRT;

/// <summary>
/// WinRT 反射通道(.NET Framework 版)—— 解析类型 / 调用成员的唯一底层工具。
///
/// .NET Framework 的 CLR <b>内建 WinRT 互操作</b>:只要按
/// <code>"类型全名, WinRT 程序集名, ContentType=WindowsRuntime"</code>
/// 这样的限定串调用 <see cref="Type.GetType(string)"/>,CLR 就会直接从操作系统
/// 解析出 WinRT 类型 —— <b>既不需要 .winmd 文件,也不需要随程序分发投影程序集</b>。
///
/// 这正是本项目选 net48 的原因:.NET 5+ 没有这个能力,
/// 必须带着 24 MB 的 <c>Microsoft.Windows.SDK.NET.dll</c> 才能拿到
/// <c>Windows.*</c> 类型,而本项目又完全靠反射调 WinRT,等于为一个程序集付全部体积。
///
/// 类型限定名的拼装统一收口在各 Api 类(见 <see cref="ConnectivityApi"/>、
/// <see cref="TetheringApi"/>),不要在别处手拼。
///
/// 错误约定:类型 / 成员不可用时<b>抛异常</b>,由调用方决定怎么记录;
/// 方法执行成功但返回 null 属于业务语义,由调用方判断。
/// </summary>
internal static class WinrtReflection
{
    /// <summary>类型缓存(限定名 → Type)。查不到也缓存 null,避免反复尝试。</summary>
    private static readonly Dictionary<string, Type> TypeCache = new Dictionary<string, Type>();

    private const BindingFlags PublicStatic = BindingFlags.Public | BindingFlags.Static;
    private const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;

    /// <summary>
    /// 解析 WinRT 类型并缓存。
    /// </summary>
    /// <param name="qualifiedName">
    /// 形如 <c>"Windows.Networking.Connectivity.NetworkInformation, Windows.Networking, ContentType=WindowsRuntime"</c>。
    /// 请用各 Api 类提供的常量,不要在别处手拼。
    /// </param>
    /// <returns>解析失败返回 null。</returns>
    public static Type FindType(string qualifiedName)
    {
        if (qualifiedName == null) return null;
        if (TypeCache.TryGetValue(qualifiedName, out var cached)) return cached;

        Type type = null;
        try { type = Type.GetType(qualifiedName); } catch { }

        TypeCache[qualifiedName] = type;
        return type;
    }

    /// <summary>
    /// 调用类型的公共静态方法。
    /// 匹配策略:先按参数类型逐个可赋值做精确匹配,匹配不到再退化为
    /// "方法名 + 参数个数"。<b>不直接用 GetMethod(名, flags)</b> ——
    /// WinRT 类型普遍有重载,那样会抛 <see cref="AmbiguousMatchException"/>。
    /// </summary>
    public static object InvokeStatic(Type type, string methodName, object[] args = null)
    {
        var method = ResolveStaticMethod(type, methodName, args);
        if (method == null) throw new MissingMethodException(type.FullName + "." + methodName);
        return method.Invoke(null, args);
    }

    /// <summary>调用实例成员,失败返回 null 而不抛异常(适合诊断路径,不打断整轮探测)。</summary>
    public static object SafeInvoke(object target, string memberName)
    {
        if (target == null) return null;
        try { return InvokeInstance(target, memberName); }
        catch { return null; }
    }

    /// <summary>读取属性值的字符串形式;取不到显示 "(null)"。</summary>
    public static string GetPropertyString(object target, string propertyName)
    {
        var value = GetProperty(target, propertyName);
        return value == null ? "(null)" : value.ToString();
    }

    // 注:早期版本这里还有一个 AwaitAsyncOperation(反射等待 IAsyncOperation)。
    // 已删除 —— 实测在 .NET Framework 上用反射调 WinRT 异步方法,拿到的是裸
    // System.__ComObject,没有 Status / Results / GetResults(),那套算法根本不成立
    // (会立刻返回空值,让人误以为操作已完成)。现在改由
    // TetheringApi.ToggleAndWait 轮询 TetheringOperationalState 判断成败。

    /// <summary>
    /// 调用实例成员,依次尝试:
    ///   1. 同名公共无参方法
    ///   2. 同名公共属性
    ///   3. 去掉 Async 后缀的同名方法 / 属性(WinRT 投影差异)
    ///   4. 大小写不敏感的同名无参方法
    /// 找不到抛 <see cref="MissingMethodException"/>。
    /// </summary>
    private static object InvokeInstance(object target, string memberName)
    {
        if (target == null) return null;
        var type = target.GetType();

        var method = FindParameterlessMethod(type, memberName);
        if (method != null) return method.Invoke(target, null);

        var property = type.GetProperty(memberName, PublicInstance);
        if (property != null) return property.GetValue(target);

        if (memberName.EndsWith("Async", StringComparison.Ordinal))
        {
            var bareName = memberName.Substring(0, memberName.Length - "Async".Length);

            method = FindParameterlessMethod(type, bareName);
            if (method != null) return method.Invoke(target, null);

            property = type.GetProperty(bareName, PublicInstance);
            if (property != null) return property.GetValue(target);
        }

        foreach (var candidate in type.GetMethods(PublicInstance))
        {
            if (string.Equals(candidate.Name, memberName, StringComparison.OrdinalIgnoreCase) &&
                candidate.GetParameters().Length == 0)
                return candidate.Invoke(target, null);
        }

        throw new MissingMethodException(type.FullName + "." + memberName);
    }

    /// <summary>读取属性值,失败返回 null。</summary>
    private static object GetProperty(object target, string propertyName)
    {
        if (target == null) return null;
        try { return target.GetType().GetProperty(propertyName)?.GetValue(target); }
        catch { return null; }
    }

    /// <summary>按名字取无参公共方法;同名重载时取第一个无参的,避免 AmbiguousMatchException。</summary>
    private static MethodInfo FindParameterlessMethod(Type type, string name)
    {
        foreach (var candidate in type.GetMethods(PublicInstance))
            if (string.Equals(candidate.Name, name, StringComparison.Ordinal) &&
                candidate.GetParameters().Length == 0)
                return candidate;

        return null;
    }

    /// <summary>
    /// 在公共静态方法里挑一个匹配的:先按参数类型精确匹配,再按"名字 + 参数个数"兜底。
    /// </summary>
    private static MethodInfo ResolveStaticMethod(Type type, string methodName, object[] args)
    {
        var wantedCount = args?.Length ?? 0;
        MethodInfo fallback = null;

        foreach (var candidate in type.GetMethods(PublicStatic))
        {
            if (!string.Equals(candidate.Name, methodName, StringComparison.Ordinal)) continue;

            var parameters = candidate.GetParameters();
            if (parameters.Length != wantedCount) continue;

            if (args == null) return candidate;

            bool exact = true;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == null) continue;
                if (!parameters[i].ParameterType.IsAssignableFrom(args[i].GetType())) { exact = false; break; }
            }

            if (exact) return candidate;
            if (fallback == null) fallback = candidate;
        }

        return fallback;
    }
}
