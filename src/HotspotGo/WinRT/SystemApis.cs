using System;
using HotspotGo.Core;

namespace HotspotGo.WinRT;

/// <summary>
/// Core 定义的两个接口在真实系统上的实现 —— 逐行转发到静态 Api 类,
/// <b>不含任何自己的逻辑</b>(逻辑在 Core,反射细节在静态 Api 类,这里只做形状转换)。
///
/// 装配只在一处发生:Program(组装根)。Core 不认识本文件,本文件认识 Core。
/// </summary>
internal sealed class SystemConnectivityApi : IConnectivityApi
{
    public object GetInternetConnectionProfile() => ConnectivityApi.GetInternetConnectionProfile();

    public object SelectShareableProfile() => ConnectivityApi.SelectShareableProfile();

    public string ReadProfileName(object profile) => ConnectivityApi.ReadProfileName(profile);

    public string ReadConnectivityLevel(object profile) => ConnectivityApi.ReadConnectivityLevel(profile);
}

/// <summary>生产实现:直接转发到 <see cref="TetheringApi"/> 与 <see cref="WinrtReflection"/>。</summary>
internal sealed class SystemTetheringApi : ITetheringApi
{
    public bool IsManagerTypeAvailable() => WinrtReflection.FindType(TetheringApi.TypeName) != null;

    public object CreateManager(object profile) => TetheringApi.CreateManager(profile);

    public string ReadState(object manager) => TetheringApi.ReadState(manager);

    public string ReadSsid(object manager) => TetheringApi.ReadSsid(manager);

    public string ReadClientCount(object manager) => TetheringApi.ReadClientCount(manager);

    public string ReadMaxClientCount(object manager) => TetheringApi.ReadMaxClientCount(manager);

    public ToggleResult ToggleAndWait(object manager, bool start, TimeSpan timeout)
        => TetheringApi.ToggleAndWait(manager, start, timeout);
}
