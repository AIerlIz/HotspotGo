using System;
using System.Collections.Generic;

namespace HotspotGo.Core;

/// <summary>
/// Core 需要的外部能力 —— 接口由**使用方(Core)**定义,实现在 WinRT 层
/// (见 WinRT/SystemApis.cs,逐行转发到静态 Api 类)。
///
/// 为什么接口放在这里而不是 WinRT 层:抽象的形状该由使用方决定。
/// 挂在实现方那一侧,签名会被底层 API 牵着走。本文件已经因此改过两次:
///   <list type="number">
///     <item>曾经有个 <c>FindManagerType()</c> 返回 <see cref="Type"/>,而 Core 只用它做过一次
///       null 判断,纯粹因为静态 Api 类原来长那样 —— 现在收成
///       <see cref="ITetheringApi.IsManagerTypeAvailable"/>。</item>
///     <item>曾经有个 <c>SelectShareableProfile()</c> 返回"已经挑好的那一条",
///       于是"优先能上外网的、没有就退而取任意已连接的"这条业务策略被迫住在
///       WinRT 层,成了整份代码里唯一没有测试覆盖的业务决策 ——
///       现在接口只回答"有哪些连接"(<see cref="IConnectivityApi.GetAllProfiles"/>),
///       挑哪条由 Core 决定。</item>
///   </list>
/// 收益:Core 不引用 WinRT 的任何类型,WinRT 反而引用 Core 的
/// <see cref="ToggleResult"/>、<see cref="HotspotState"/>、<see cref="ConnectivityLevel"/>,
/// 依赖方向单向朝内。
/// 新增一个 WinRT 操作要改四处:静态 Api 类 → 本文件的接口 → WinRT/SystemApis.cs 的转发
/// → 测试里的 Fake*。
/// </summary>
internal interface IConnectivityApi
{
    /// <summary>取当前 Internet 连接配置;null = 系统认为当前没有 Internet 连接。</summary>
    object GetInternetConnectionProfile();

    /// <summary>
    /// 取系统里全部连接配置(含未连接的,由 Core 自己筛)。
    ///
    /// 只回答"有哪些连接",不决定"该用哪条" —— 挑选规则是业务判断,归 Core
    /// (见 <see cref="HotspotService"/> 里等上游那一段)。这样"哪条连接能开热点"
    /// 才能在单元测试里复现,而不是只能在真机上试。
    /// </summary>
    IEnumerable<object> GetAllProfiles();

    /// <summary>读连接配置名(如 PPPoE);读不到返回 "(null)"。</summary>
    string ReadProfileName(object profile);

    /// <summary>
    /// 读连接等级(InternetAccess / LocalAccess / ...);读不到返回 "(null)"。
    /// 判定用的字面量见 <see cref="ConnectivityLevel"/>。
    /// </summary>
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

    /// <summary>
    /// 开 / 关热点,并等到实际状态达到目标才返回。
    /// "达到目标"的判定在 Core(<see cref="HotspotState.IsTargetReached"/>),
    /// 因为那是业务判断,而且放在 Core 才测得到。
    /// </summary>
    ToggleResult ToggleAndWait(object manager, bool start, TimeSpan timeout);
}
