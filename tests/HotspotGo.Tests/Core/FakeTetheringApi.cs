using System;
using HotspotGo.Core;

namespace HotspotGo.Tests.Core;

/// <summary>
/// 假的热点管理器操作:状态存在一个字段里,开 / 关时就地改写,
/// 于是"开完之后状态到底变没变、退出码对不对"都能直接断言。
/// </summary>
internal sealed class FakeTetheringApi : ITetheringApi
{
    /// <summary>系统里能不能解析到热点管理器类型;置 false 模拟这个 Windows 用不了热点 API。</summary>
    public bool ManagerTypeAvailable { get; set; } = true;

    /// <summary>CreateManager 的返回值;置 null = 该上游不能作为热点共享源。</summary>
    public object Manager { get; set; } = new object();

    /// <summary>CreateManager 要抛出的异常;null = 不抛。</summary>
    public Exception CreateError { get; set; }

    /// <summary>当前热点状态(ReadState 的返回值)。</summary>
    public string State { get; set; } = "Off";

    /// <summary>SSID 与客户端数。</summary>
    public string Ssid { get; set; } = "MY-SSID";

    public string ClientCount { get; set; } = "0";

    public string MaxClientCount { get; set; } = "8";

    /// <summary>
    /// ToggleAndWait 的替代实现;默认按"状态确实变了"返回成功。
    /// 想让开 / 关失败就换掉它。
    /// </summary>
    public Func<object, bool, ToggleResult> ToggleHandler { get; set; }

    /// <summary>ToggleAndWait 被调用的次数。</summary>
    public int ToggleCallCount { get; private set; }

    /// <summary>最后一次开 / 关的方向(true = 开)。</summary>
    public bool? LastToggleStart { get; private set; }

    /// <summary>最后一次传给 ToggleAndWait 的等待上限。</summary>
    public TimeSpan? LastToggleTimeout { get; private set; }

    /// <summary>CreateManager 被调用的次数。</summary>
    public int CreateCallCount { get; private set; }

    /// <summary>最后一次交给 CreateManager 的连接配置。</summary>
    public object LastCreatedProfile { get; private set; }

    public FakeTetheringApi()
    {
        ToggleHandler = (manager, start) =>
        {
            string before = State;
            State = start ? HotspotState.On : "Off";
            return ToggleResult.Success(before, State, TimeSpan.Zero);
        };
    }

    public bool IsManagerTypeAvailable() => ManagerTypeAvailable;

    public object CreateManager(object profile)
    {
        CreateCallCount++;
        LastCreatedProfile = profile;
        if (CreateError != null) throw CreateError;
        return Manager;
    }

    public string ReadState(object manager) => State;

    public string ReadSsid(object manager) => Ssid;

    public string ReadClientCount(object manager) => ClientCount;

    public string ReadMaxClientCount(object manager) => MaxClientCount;

    public ToggleResult ToggleAndWait(object manager, bool start, TimeSpan timeout)
    {
        ToggleCallCount++;
        LastToggleStart = start;
        LastToggleTimeout = timeout;
        return ToggleHandler(manager, start);
    }
}
