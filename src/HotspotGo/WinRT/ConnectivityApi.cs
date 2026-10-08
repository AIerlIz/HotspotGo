using System;
using System.Collections;
using System.Collections.Generic;
using HotspotGo.Core;

namespace HotspotGo.WinRT;

/// <summary>
/// <c>Windows.Networking.Connectivity.NetworkInformation</c> 的反射封装 ——
/// 回答"系统当前有哪些连接、这条连接什么等级"。
///
/// 本层<b>只提供事实,不做选择</b>:"优先能上外网的、没有就退而取任意已连接的"
/// 是业务策略,归 Core(见 <c>HotspotService</c> 里挑选共享源那一段)。
/// 这里曾经有个 <c>SelectShareableProfile()</c> 把策略一起包了,结果是
/// "热点挂到哪条连接上"这个决定成了唯一测不到的业务判断。
///
/// 工具本身不依赖连接叫什么名字:问系统要连接配置,再把它作为热点的共享源。
/// PPPoE 改名、或上游换成 Wi-Fi / 以太网,这里都不用改。
/// </summary>
internal static class ConnectivityApi
{
    /// <summary>Internet 连接信息类型的全名(不含限定后缀,报错信息用)。</summary>
    public const string TypeFullName =
        "Windows.Networking.Connectivity.NetworkInformation";

    /// <summary>Internet 连接信息类型的限定名(可直接交给反射解析)。</summary>
    public const string TypeName =
        TypeFullName + ", Windows.Networking, ContentType=WindowsRuntime";

    /// <summary>
    /// 取当前 Internet 连接配置(ConnectionProfile)。
    /// 返回 null 表示系统认为当前没有 Internet 连接(业务语义,不是错误)。
    /// </summary>
    public static object GetInternetConnectionProfile()
    {
        var type = WinrtReflection.FindType(TypeName);
        if (type == null) throw new TypeLoadException("找不到 WinRT 类型: " + TypeFullName);

        return WinrtReflection.InvokeStatic(type, "GetInternetConnectionProfile");
    }

    /// <summary>
    /// 取系统里全部连接配置(含未连接的 —— 筛不筛是调用方的事)。
    ///
    /// 类型解析失败 / 返回值不可枚举这两类问题在这里<b>立刻抛出</b>(不是等迭代时才抛),
    /// 由 Core 兜住并翻成"没有可用连接"的退出码 —— 那里才决定"抛异常意味着什么"。
    ///
    /// WinRT 的 <c>IVectorView</c> 在这里已被 CLR 投影成实现了 <see cref="IEnumerable"/>
    /// 的托管集合,因此可以直接遍历。
    /// </summary>
    public static IEnumerable<object> GetAllProfiles()
    {
        var type = WinrtReflection.FindType(TypeName);
        if (type == null) throw new TypeLoadException("找不到 WinRT 类型: " + TypeFullName);

        object profiles = WinrtReflection.InvokeStatic(type, "GetConnectionProfiles");

        if (profiles is IEnumerable enumerable) return Enumerate(enumerable);

        throw new InvalidOperationException("GetConnectionProfiles 返回的对象不可枚举");
    }

    /// <summary>读连接配置名(如 PPPoE);读不到返回 "(null)"。</summary>
    public static string ReadProfileName(object profile)
        => WinrtReflection.GetPropertyString(profile, "ProfileName");

    /// <summary>读连接等级(InternetAccess / LocalAccess / ...);读不到返回 "(null)"。</summary>
    public static string ReadConnectivityLevel(object profile)
        => DescribeLevel(profile);

    /// <summary>把 GetNetworkConnectivityLevel 的枚举值读成文本。</summary>
    private static string DescribeLevel(object profile)
    {
        var level = WinrtReflection.SafeInvoke(profile, "GetNetworkConnectivityLevel");
        return level == null ? UnknownValue.Placeholder : level.ToString();
    }

    /// <summary>
    /// 把投影出来的集合收成 <see cref="IEnumerable{T}"/>。
    /// 单独拆一个方法是为了让上面的类型检查与"不可枚举"判断立即执行 ——
    /// 迭代器方法体里的异常要等到开始遍历才抛,那样错误会在 Core 的循环里才冒出来。
    /// </summary>
    private static IEnumerable<object> Enumerate(IEnumerable source)
    {
        foreach (var item in source) yield return item;
    }
}
