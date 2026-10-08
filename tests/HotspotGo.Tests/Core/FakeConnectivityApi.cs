using System;
using System.Collections.Generic;
using HotspotGo.Core;

namespace HotspotGo.Tests.Core;

/// <summary>
/// 假的连接查询:返回值可预设、调用次数可断言。
///
/// 用来隔离"系统当前有没有网"这个外部状态 —— 真实实现去问 WinRT 要连接配置,
/// 结果取决于跑测试的机器,那样的断言没有意义。
///
/// <see cref="GetAllProfiles"/> 吐出的每条连接都带自己的等级,于是
/// "优先能上外网的、没有就退而取任意已连接的"这条挑选规则可以在测试里完整复现 ——
/// 它以前住在 WinRT 层,是整份代码里唯一没有测试覆盖的业务决策。
/// </summary>
internal sealed class FakeConnectivityApi : IConnectivityApi
{
    private readonly Queue<object> _pending = new Queue<object>();

    /// <summary>排队都用完之后的兜底返回值;置 null = 当前没有 Internet 连接。</summary>
    public object DefaultProfile { get; set; } = new object();

    /// <summary>ReadProfileName 对不带名字的 profile 的返回值。</summary>
    public string DefaultProfileName { get; set; } = "PPPoE";

    /// <summary>ReadConnectivityLevel 对不带等级的 profile 的返回值。</summary>
    public string DefaultConnectivityLevel { get; set; } = ConnectivityLevel.InternetAccess;

    /// <summary>GetAllProfiles 要吐出的连接(--any 的挑选源);空 = 一个已连接的网卡都没有。</summary>
    public List<FakeProfile> Profiles { get; } = new List<FakeProfile>();

    /// <summary>
    /// GetAllProfiles 要抛出的异常;null = 不抛。
    /// 用来模拟"这台机器解析不到 WinRT 类型"——真实实现是在调用时立刻抛的。
    /// </summary>
    public Exception ProfilesError { get; set; }

    /// <summary>GetInternetConnectionProfile 被调用的次数 —— 轮询次数靠它断言。</summary>
    public int QueryCount { get; private set; }

    /// <summary>GetAllProfiles 被调用的次数(--any 专用路径)。</summary>
    public int EnumerationCount { get; private set; }

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

    /// <summary>加一条候选连接;返回它,便于断言"最后选中的就是这一条"。</summary>
    public FakeProfile AddProfile(string level, string name = "PPPoE")
    {
        var profile = new FakeProfile { Level = level, Name = name };
        Profiles.Add(profile);
        return profile;
    }

    public object GetInternetConnectionProfile()
    {
        QueryCount++;

        object next = _pending.Count > 0 ? _pending.Dequeue() : DefaultProfile;
        if (next is Exception error) throw error;
        return next;
    }

    public IEnumerable<object> GetAllProfiles()
    {
        EnumerationCount++;
        if (ProfilesError != null) throw ProfilesError;
        return Profiles;
    }

    public string ReadProfileName(object profile)
    {
        LastNamedProfile = profile;
        return (profile as FakeProfile)?.Name ?? DefaultProfileName;
    }

    public string ReadConnectivityLevel(object profile)
    {
        var fake = profile as FakeProfile;
        if (fake?.LevelError != null) throw fake.LevelError;
        return fake?.Level ?? DefaultConnectivityLevel;
    }
}

/// <summary>一条假连接:带名字与等级,用来复现 --any 的挑选过程。</summary>
internal sealed class FakeProfile
{
    /// <summary>连接名(日志里那几行)。</summary>
    public string Name { get; set; } = "PPPoE";

    /// <summary>连接等级;默认给个能上外网的。</summary>
    public string Level { get; set; } = ConnectivityLevel.InternetAccess;

    /// <summary>
    /// 读这条连接的等级时抛出的异常;null = 不抛。
    /// 用来验证"某一条查不通"不会把整轮挑选带走。
    /// </summary>
    public Exception LevelError { get; set; }
}
