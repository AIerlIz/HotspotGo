using System;
using HotspotGo.Core;
using Xunit;

namespace HotspotGo.Tests.Core;

/// <summary>
/// 上游轮询。
///
/// 这段逻辑的特点是"必须至少查一次、单次失败不算致命、超时就放弃",
/// 三条都是排障时容易搞错的地方(比如误以为超时 0 就不查了),
/// 所以用毫秒级间隔把语义钉死,测试本身不依赖系统有没有网。
/// </summary>
public class UpstreamMonitorTests
{
    /// <summary>不等待:只查询,拿到就拿,拿不到立刻返回 null。</summary>
    private static readonly TimeSpan NoWait = TimeSpan.Zero;

    /// <summary>测试里不需要真的等,间隔取 1 毫秒保证快速返回。</summary>
    private static readonly TimeSpan TinyInterval = TimeSpan.FromMilliseconds(1);

    /// <summary>造一台"已经被问到"的假连接源。</summary>
    private static UpstreamMonitor MonitorOver(FakeConnectivityApi connectivity)
        => new UpstreamMonitor(new FakeLogger(), connectivity);

    /// <summary>上游已就绪 → 第一次查询就返回它,不额外轮询。</summary>
    [Fact]
    public void Returns_profile_immediately_when_ready()
    {
        var profile = new object();
        var connectivity = new FakeConnectivityApi { DefaultProfile = profile };

        Assert.Same(profile, MonitorOver(connectivity).WaitForInternetProfile(TimeSpan.FromSeconds(5), TinyInterval));
        Assert.Equal(1, connectivity.QueryCount);
    }

    /// <summary>超时为 0 也至少查一次 —— "至少尝试一次"是明确约定。</summary>
    [Fact]
    public void Always_queries_at_least_once()
    {
        var connectivity = new FakeConnectivityApi { DefaultProfile = null };

        Assert.Null(MonitorOver(connectivity).WaitForInternetProfile(NoWait, TinyInterval));
        Assert.Equal(1, connectivity.QueryCount);
    }

    /// <summary>上游稍后才就绪 → 一直轮询到拿到为止,超时前不会放弃。</summary>
    [Fact]
    public void Keeps_polling_until_upstream_becomes_ready()
    {
        var profile = new object();
        var connectivity = new FakeConnectivityApi { DefaultProfile = null }
            .Enqueue(null, null, profile);

        Assert.Same(profile, MonitorOver(connectivity).WaitForInternetProfile(TimeSpan.FromSeconds(5), TinyInterval));
        Assert.Equal(3, connectivity.QueryCount);
    }

    /// <summary>超时仍没有 → 返回 null(不抛异常,由调用方翻成退出码 2)。</summary>
    [Fact]
    public void Returns_null_on_timeout()
    {
        var connectivity = new FakeConnectivityApi { DefaultProfile = null };

        Assert.Null(MonitorOver(connectivity).WaitForInternetProfile(NoWait, TinyInterval));
    }

    /// <summary>单次查询抛异常只记日志、不算致命:整轮等待会继续,直到拿到上游。</summary>
    [Fact]
    public void Survives_a_failing_query()
    {
        var log = new FakeLogger();
        var profile = new object();
        var connectivity = new FakeConnectivityApi
        {
            DefaultProfile = null,
        }.Enqueue(new InvalidOperationException("模拟查询失败"), profile);

        var result = new UpstreamMonitor(log, connectivity)
            .WaitForInternetProfile(TimeSpan.FromSeconds(5), TinyInterval);

        Assert.Same(profile, result);
        Assert.Equal(2, connectivity.QueryCount);
        Assert.Contains("模拟查询失败", log.Text);
    }

    /// <summary>等待结果要落日志:拿到上游时写明用时,事后能看出"到底等了多久"。</summary>
    [Fact]
    public void Logs_when_upstream_is_ready()
    {
        var log = new FakeLogger();
        var connectivity = new FakeConnectivityApi();

        new UpstreamMonitor(log, connectivity).WaitForInternetProfile(NoWait, TinyInterval);

        Assert.Contains("上游已就绪", log.Text);
        Assert.Contains("PPPoE", log.Text);
        Assert.Contains("秒)", log.Text);
    }
}
