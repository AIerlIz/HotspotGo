using System;

namespace HotspotGo.Core;

/// <summary>
/// Core 需要的外部能力 —— 接口由**使用方(Core)**定义,实现在 WinRT 层
/// (见 WinRT/SystemApis.cs,逐行转发到静态 Api 类)。
///
/// 为什么接口放在这里而不是 WinRT 层:抽象的形状该由使用方决定。
/// 挂在实现方那一侧,签名会被底层 API 牵着走 —— 之前这里有个
/// <c>FindManagerType()</c> 返回 <see cref="Type"/>,而 Core 只用它做了一次 null 判断,
/// 纯粹因为静态 Api 类原来长那样。现在收成 <see cref="ITetheringApi.IsManagerTypeAvailable"/>。
///
/// 收益:Core 不引用 WinRT 的任何类型,WinRT 反而引用 Core 的
/// <see cref="ToggleResult"/> 与 <see cref="HotspotState"/>,依赖方向单向朝内。
/// 新增一个 WinRT 操作要改四处:静态 Api 类 → 本文件的接口 → WinRT/SystemApis.cs 的转发
/// → 测试里的 Fake*。
/// </summary>
internal interface IConnectivityApi
{
    /// <summary>取当前 Internet 连接配置;null = 系统认为当前没有 Internet 连接。</summary>
    object GetInternetConnectionProfile();

    /// <summary>在所有连接里挑一条可用于开热点的;null = 没有任何已连接的网卡。</summary>
    object SelectShareableProfile();

    /// <summary>读连接配置名(如 PPPoE);读不到返回 "(null)"。</summary>
    string ReadProfileName(object profile);

    /// <summary>读连接等级(InternetAccess / LocalAccess / ...);读不到返回 "(null)"。</summary>
    string ReadConnectivityLevel(object profile);
}

/// <summary>热点管理器相关的操作。</summary>
internal interface ITetheringApi
{
    /// <summary>系统里能不能解析到热点管理器类型。false = 这个 Windows 用不了热点 API。</summary>
    bool IsManagerTypeAvailable();

    /// <summary>用连接配置创建热点管理器;null = 该上游不能作为热点共享源。</summary>
    object CreateManager(object profile);

    /// <summary>读热点运行状态(Off / On / ...);读不到返回 "(null)"。</summary>
    string ReadState(object manager);

    /// <summary>读当前 SSID;读不到返回 "(null)"。</summary>
    string ReadSsid(object manager);

    /// <summary>读当前已连接客户端数;读不到返回 "(null)"。</summary>
    string ReadClientCount(object manager);

    /// <summary>读最大客户端数;读不到返回 "(null)"。</summary>
    string ReadMaxClientCount(object manager);

    /// <summary>开 / 关热点,并等到实际状态变成目标值才返回。</summary>
    ToggleResult ToggleAndWait(object manager, bool start, TimeSpan timeout);
}
