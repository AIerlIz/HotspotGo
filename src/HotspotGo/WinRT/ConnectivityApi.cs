using System;
using System.Collections;
using System.Collections.Generic;

namespace HotspotGo.WinRT;

/// <summary>
/// <c>Windows.Networking.Connectivity.NetworkInformation</c> 的反射封装 ——
/// 回答"系统当前能用哪条连接"。
///
/// 工具本身不依赖连接叫什么名字:问系统要一条连接配置,再把它作为热点的共享源。
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

    /// <summary>连接等级:完全未连接,不能作为共享源。</summary>
    private const string LevelNone = "None";

    /// <summary>连接等级:能出外网。</summary>
    private const string LevelInternet = "InternetAccess";

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
    /// 在系统所有连接里挑一条可用于开热点的:<b>优先能上外网的</b>,没有就退而求其次取
    /// 任意一条已连接的(有本地链路即可,例如网线接了但路由器没通)。
    /// 全都没有(所有网卡都断开)时返回 null —— 此时热点 API 无从挂载,开不了。
    /// </summary>
    public static object SelectShareableProfile()
    {
        object fallback = null;

        foreach (var profile in GetAllConnectionProfiles())
        {
            if (!IsConnected(profile)) continue;

            if (IsInternetAccess(profile)) return profile;
            if (fallback == null) fallback = profile;
        }

        return fallback;
    }

    /// <summary>读连接配置名(如 PPPoE);读不到返回 "(null)"。</summary>
    public static string ReadProfileName(object profile)
        => WinrtReflection.GetPropertyString(profile, "ProfileName");

    /// <summary>读连接等级(InternetAccess / LocalAccess / ...);读不到返回 "(null)"。</summary>
    public static string ReadConnectivityLevel(object profile)
        => DescribeLevel(profile);

    /// <summary>
    /// 取系统里全部连接配置。WinRT 的 <c>IVectorView</c> 在这里已被 CLR 投影成
    /// 实现了 <see cref="IEnumerable"/> 的托管集合,因此可以直接遍历。
    /// </summary>
    private static IEnumerable<object> GetAllConnectionProfiles()
    {
        var type = WinrtReflection.FindType(TypeName);
        if (type == null) throw new TypeLoadException("找不到 WinRT 类型: " + TypeFullName);

        object profiles = WinrtReflection.InvokeStatic(type, "GetConnectionProfiles");

        if (profiles is IEnumerable enumerable)
        {
            foreach (var profile in enumerable) yield return profile;
            yield break;
        }

        throw new InvalidOperationException("GetConnectionProfiles 返回的对象不可枚举");
    }

    /// <summary>连接等级不是 None 就算已连接(有本地链路即可,不需要能出外网)。</summary>
    private static bool IsConnected(object profile)
        => DescribeLevel(profile) != LevelNone;

    /// <summary>是否真的能出外网。</summary>
    private static bool IsInternetAccess(object profile)
        => DescribeLevel(profile) == LevelInternet;

    /// <summary>把 GetNetworkConnectivityLevel 的枚举值读成文本。</summary>
    private static string DescribeLevel(object profile)
    {
        var level = WinrtReflection.SafeInvoke(profile, "GetNetworkConnectivityLevel");
        return level == null ? "(null)" : level.ToString();
    }
}
