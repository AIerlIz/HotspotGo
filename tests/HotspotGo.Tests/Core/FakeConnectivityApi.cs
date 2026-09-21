using System;
using System.Collections.Generic;
using HotspotGo.Core;

namespace HotspotGo.Tests.Core;

/// <summary>
/// 假的连接查询:返回值可预设、调用次数可断言。
///
/// 用来隔离"系统当前有没有网"这个外部状态 —— 真实实现去问 WinRT 要连接配置,
/// 结果取决于跑测试的机器,那样的断言没有意义。
/// </summary>
internal sealed class FakeConnectivityApi : IConnectivityApi
{
    private readonly Queue<object> _pending = new Queue<object>();

    /// <summary>排队都用完之后的兜底返回值;置 null = 当前没有 Internet 连接。</summary>
    public object DefaultProfile { get; set; } = new object();

    /// <summary>SelectShareableProfile 的返回值;置 null = 一个已连接的网卡都没有。</summary>
    public object ShareableProfile { get; set; } = new object();

    /// <summary>连接配置名(--status 日志里的那一行)。</summary>
    public string ProfileName { get; set; } = "PPPoE";

    /// <summary>连接等级(InternetAccess / LocalAccess / None)。</summary>
    public string ConnectivityLevel { get; set; } = "InternetAccess";

    /// <summary>GetInternetConnectionProfile 被调用的次数 —— 轮询次数靠它断言。</summary>
    public int QueryCount { get; private set; }

    /// <summary>SelectShareableProfile 被调用的次数(--any 专用路径)。</summary>
    public int SelectCount { get; private set; }

    /// <summary>最后一次被读名字的连接配置 —— 用来确认用的是哪条 profile。</summary>
    public object LastNamedProfile { get; private set; }

    /// <summary>
    /// 排队若干次查询结果,模拟"上游稍后才就绪";排队用完之前不看 <see cref="DefaultProfile"/>。
    /// 排进去一个 <see cref="Exception"/> 表示"这一次查询抛异常"(某一轮没问到)——
    /// 用来验证单次失败不打断整轮等待。
    /// </summary>
    public FakeConnectivityApi Enqueue(params object[] responses)
    {
        foreach (var response in responses) _pending.Enqueue(response);
        return this;
    }

    public object GetInternetConnectionProfile()
    {
        QueryCount++;

        object next = _pending.Count > 0 ? _pending.Dequeue() : DefaultProfile;
        if (next is Exception error) throw error;
        return next;
    }

    public object SelectShareableProfile()
    {
        SelectCount++;
        return ShareableProfile;
    }

    public string ReadProfileName(object profile)
    {
        LastNamedProfile = profile;
        return ProfileName;
    }

    public string ReadConnectivityLevel(object profile) => ConnectivityLevel;
}
