using System;
using System.Diagnostics;
using System.Threading;
using HotspotGo.Logging;

namespace HotspotGo.Core;

/// <summary>
/// 上游(拨号)就绪监测:轮询"当前 Internet 连接配置",拿到非空结果或超时为止。
///
/// 热点的共享源必须是"已联网"的连接,所以开热点前要先等到它。
/// 开 / 关两种命令共用同一套轮询逻辑,只是等待策略不同(见 <see cref="HotspotService"/>)。
///
/// 连接查询走 <see cref="IConnectivityApi"/>,本层不认识 WinRT ——
/// 测试里换成假实现,就能复现"一直查不到 / 查一次抛异常 / 第三次才就绪"这些情况。
/// </summary>
internal sealed class UpstreamMonitor
{
    private readonly ILogger _log;
    private readonly IConnectivityApi _connectivity;

    public UpstreamMonitor(ILogger log, IConnectivityApi connectivity)
    {
        _log = log;
        _connectivity = connectivity;
    }

    /// <summary>
    /// 轮询取 Internet 连接配置,拿到非空结果立即返回;超时仍没有则返回 null。
    /// <b>至少尝试一次</b>(超时为 0 也会查一次)。单次查询失败只记日志,不算致命。
    /// </summary>
    /// <param name="timeout">最长等待时长。</param>
    /// <param name="pollInterval">两次查询之间的间隔。</param>
    public object WaitForInternetProfile(TimeSpan timeout, TimeSpan pollInterval)
    {
        var elapsed = Stopwatch.StartNew();

        while (true)
        {
            var profile = TryQueryProfile();
            if (profile != null)
            {
                _log.WriteLine("上游已就绪: " + _connectivity.ReadProfileName(profile) +
                    "(用时 " + (int)elapsed.Elapsed.TotalSeconds + " 秒)");
                return profile;
            }

            if (elapsed.Elapsed >= timeout)
                return null;

            Thread.Sleep(pollInterval);
        }
    }

    /// <summary>调一次 GetInternetConnectionProfile;失败记日志并返回 null,不抛出。</summary>
    private object TryQueryProfile()
    {
        try
        {
            return _connectivity.GetInternetConnectionProfile();
        }
        catch (Exception ex)
        {
            _log.WriteLine("[错误] GetInternetConnectionProfile: " + ex.Message);
            return null;
        }
    }
}
