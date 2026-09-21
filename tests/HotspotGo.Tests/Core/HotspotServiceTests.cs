using System;
using System.Diagnostics;
using HotspotGo.Cli;
using HotspotGo.Core;
using Xunit;

namespace HotspotGo.Tests.Core;

/// <summary>
/// 业务编排(<see cref="HotspotService.Run"/>)的分支与退出码。
///
/// 这是本工具真正的核心逻辑:等上游 → 建管理器 → 读状态 → 开 / 关;
/// 每个岔路口都对应一个退出码,计划任务和脚本据此判断成败,所以逐条钉死。
/// 所有 WinRT 操作由 Fake* 提供,不依赖跑测试的机器有没有网卡、热点开着没。
/// </summary>
public class HotspotServiceTests
{
    private readonly FakeLogger _log = new FakeLogger();
    private readonly FakeConnectivityApi _connectivity = new FakeConnectivityApi();
    private readonly FakeTetheringApi _tethering = new FakeTetheringApi();

    /// <summary>装配被测对象。Options 一律走真实解析,免得测试自己造出生产里不会出现的参数组合。</summary>
    private HotspotService Service() => new HotspotService(_log, _connectivity, _tethering);

    // ===================================================================
    // 开启
    // ===================================================================

    /// <summary>热点本来就是开的 → 直接跳过,不调 StartTetheringAsync,退出码 0。</summary>
    [Fact]
    public void TurnOn_skips_when_already_on()
    {
        _tethering.State = HotspotState.On;

        var code = Service().Run(Options.Parse(new string[0]));

        Assert.Equal(ExitCode.Ok, code);
        Assert.Equal(0, _tethering.ToggleCallCount);
        Assert.Contains("[跳过]", _log.Text);
    }

    /// <summary>开启成功 → 退出码 0,方向为"开",等待上限 30 秒。</summary>
    [Fact]
    public void TurnOn_succeeds_and_returns_ok()
    {
        var code = Service().Run(Options.Parse(new string[0]));

        Assert.Equal(ExitCode.Ok, code);
        Assert.Equal(1, _tethering.ToggleCallCount);
        Assert.True(_tethering.LastToggleStart);
        Assert.Equal(TimeSpan.FromSeconds(30), _tethering.LastToggleTimeout);
        Assert.Equal(HotspotState.On, _tethering.State);
        Assert.Contains("[成功] 热点已开启", _log.Text);
    }

    /// <summary>开启没能达标 → 退出码 4(这是唯一代表"开启失败"的退出码)。</summary>
    [Fact]
    public void TurnOn_returns_StartFailed_when_toggle_fails()
    {
        _tethering.ToggleHandler = (manager, start) => ToggleResult.Failure("Off", "状态没变");

        var code = Service().Run(Options.Parse(new string[0]));

        Assert.Equal(ExitCode.StartFailed, code);
        Assert.Contains("状态没变", _log.Text);
        Assert.Contains("[失败] 开启热点未成功", _log.Text);
    }

    /// <summary>开 / 关过程抛异常 → 折叠成一次失败结果,不往主流程外抛,退出码仍是 4。</summary>
    [Fact]
    public void TurnOn_returns_StartFailed_when_toggle_throws()
    {
        _tethering.ToggleHandler = (manager, start) => throw new InvalidOperationException("模拟开热点异常");

        var code = Service().Run(Options.Parse(new string[0]));

        Assert.Equal(ExitCode.StartFailed, code);
        Assert.Contains("模拟开热点异常", _log.Text);
    }

    // ===================================================================
    // 关闭
    // ===================================================================

    /// <summary>热点本来就关着 → 跳过,不调 StopTetheringAsync。</summary>
    [Fact]
    public void TurnOff_skips_when_already_off()
    {
        var code = Service().Run(Options.Parse(new[] { "--off" }));

        Assert.Equal(ExitCode.Ok, code);
        Assert.Equal(0, _tethering.ToggleCallCount);
        Assert.Contains("热点本来就没开", _log.Text);
    }

    /// <summary>正常关闭 → 方向为"关",状态落到 Off。</summary>
    [Fact]
    public void TurnOff_stops_running_hotspot()
    {
        _tethering.State = HotspotState.On;

        var code = Service().Run(Options.Parse(new[] { "--off" }));

        Assert.Equal(ExitCode.Ok, code);
        Assert.Equal(1, _tethering.ToggleCallCount);
        Assert.False(_tethering.LastToggleStart);
        Assert.Equal("Off", _tethering.State);
    }

    /// <summary>
    /// 关闭失败仍返回 0 —— 有意为之:关不掉没什么后果,不该让计划任务报"出错"。
    /// 结果只写进日志。这条是契约,不是遗漏。
    /// </summary>
    [Fact]
    public void TurnOff_still_returns_ok_when_toggle_fails()
    {
        _tethering.State = HotspotState.On;
        _tethering.ToggleHandler = (manager, start) => ToggleResult.Failure("On", "关不掉");

        var code = Service().Run(Options.Parse(new[] { "--off" }));

        Assert.Equal(ExitCode.Ok, code);
        Assert.Contains("关不掉", _log.Text);
    }

    // ===================================================================
    // 只读状态
    // ===================================================================

    /// <summary>--status 只读:绝不开 / 关,退出码 0。</summary>
    [Fact]
    public void Status_never_toggles()
    {
        _tethering.State = HotspotState.On;

        var code = Service().Run(Options.Parse(new[] { "--status" }));

        Assert.Equal(ExitCode.Ok, code);
        Assert.Equal(0, _tethering.ToggleCallCount);
        Assert.Contains("--status 模式", _log.Text);
    }

    /// <summary>状态、SSID、客户端数都要写进日志 —— 事后排障全靠这几行。</summary>
    [Fact]
    public void Status_reports_state_ssid_and_clients()
    {
        _tethering.State = HotspotState.On;
        _tethering.Ssid = "HOTSPOT-1";
        _tethering.ClientCount = "2";
        _tethering.MaxClientCount = "8";

        Service().Run(Options.Parse(new[] { "--status" }));

        Assert.Contains("当前热点状态: On", _log.Text);
        Assert.Contains("HOTSPOT-1", _log.Text);
        Assert.Contains("2 / 8", _log.Text);
    }

    // ===================================================================
    // --any:不等外网
    // ===================================================================

    /// <summary>--any 直接从全部连接里挑一条,完全不走"等 Internet 连接"的轮询。</summary>
    [Fact]
    public void Any_picks_shareable_profile_without_waiting()
    {
        _connectivity.DefaultProfile = null;            // 没有 Internet 连接
        _connectivity.ShareableProfile = new object();  // 但有已连接的网卡

        var code = Service().Run(Options.Parse(new[] { "--any" }));

        Assert.Equal(ExitCode.Ok, code);
        Assert.Equal(1, _connectivity.SelectCount);
        Assert.Equal(0, _connectivity.QueryCount);
        Assert.Same(_connectivity.ShareableProfile, _connectivity.LastNamedProfile);
    }

    /// <summary>--any 但一个已连接的网卡都没有 → 退出码 2,提示先接网线 / 连 Wi-Fi。</summary>
    [Fact]
    public void Any_without_any_connection_returns_NoUpstream()
    {
        _connectivity.ShareableProfile = null;

        var code = Service().Run(Options.Parse(new[] { "--any" }));

        Assert.Equal(ExitCode.NoUpstream, code);
        Assert.Contains("没有任何已连接的网卡", _log.Text);
    }

    /// <summary>--any 只影响"开"这个动作,--off / --status 的路径不受它影响。</summary>
    [Fact]
    public void Any_does_not_change_turn_off_or_status_paths()
    {
        Assert.Equal(ExitCode.Ok, Service().Run(Options.Parse(new[] { "--off", "--any" })));
        Assert.Equal(ExitCode.Ok, Service().Run(Options.Parse(new[] { "--status", "--any" })));

        Assert.Equal(0, _connectivity.SelectCount);
    }

    // ===================================================================
    // 拿不到上游 / 拿不到管理器
    // ===================================================================

    /// <summary>等不到上游 → 退出码 2;--wait 0 时只查一次就放弃,不会白等 90 秒。</summary>
    [Fact]
    public void Missing_upstream_returns_NoUpstream_after_single_query()
    {
        _connectivity.DefaultProfile = null;

        var code = Service().Run(Options.Parse(new[] { "--wait", "0" }));

        Assert.Equal(ExitCode.NoUpstream, code);
        Assert.Equal(1, _connectivity.QueryCount);
        Assert.Contains("超时:始终没有 Internet 连接配置文件", _log.Text);
        Assert.Equal(0, _tethering.CreateCallCount);
    }

    /// <summary>
    /// --off:上游可能已经断开,所以只快速扫一遍(3 秒 / 500ms)就放弃,
    /// 不能套用常规模式的 90 秒等待 —— 那样关机 / 注销时会被拖住。
    /// 顺带锁住它专属的失败文案。
    ///
    /// 注意这条是**唯一带墙钟判断**的用例,能锁的和不能锁的要分清:
    ///   - 能锁:总耗时远小于常规模式的 90 秒;且确实重试了(不是查一次就放弃);
    ///   - 不能锁:500ms 这个间隔本身 —— 2 秒间隔同样会在 3 秒上限处退出,
    ///     两者的耗时区间重叠,再收紧阈值只会换来偶发失败。间隔靠人工审阅。
    /// </summary>
    [Fact]
    public void TurnOff_gives_up_quickly_when_no_upstream()
    {
        _connectivity.DefaultProfile = null;
        var stopwatch = Stopwatch.StartNew();

        var code = Service().Run(Options.Parse(new[] { "--off" }));

        stopwatch.Stop();
        Assert.Equal(ExitCode.NoUpstream, code);
        Assert.Contains("--off 模式下也拿不到", _log.Text);
        Assert.True(_connectivity.QueryCount >= 2,
            "应反复重试,而不是只查一次;实际 " + _connectivity.QueryCount);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(4.5),
            "应 3 秒左右就放弃,而不是套用常规模式的 90 秒;实际 " + stopwatch.Elapsed);
    }

    /// <summary>系统里解析不到 TetheringManager 类型 → 退出码 3,且不去建管理器。</summary>
    [Fact]
    public void Missing_manager_type_returns_NoManager()
    {
        _tethering.ManagerTypeAvailable = false;

        var code = Service().Run(Options.Parse(new string[0]));

        Assert.Equal(ExitCode.NoManager, code);
        Assert.Equal(0, _tethering.CreateCallCount);
        Assert.Contains("找不到 TetheringManager 类型", _log.Text);
    }

    /// <summary>CreateFromConnectionProfile 返回 null(该上游不能做共享源)→ 退出码 3。</summary>
    [Fact]
    public void Null_manager_returns_NoManager()
    {
        _tethering.Manager = null;

        var code = Service().Run(Options.Parse(new string[0]));

        Assert.Equal(ExitCode.NoManager, code);
        Assert.Contains("CreateFromConnectionProfile 返回 null", _log.Text);
    }

    /// <summary>建管理器时抛异常 → 记日志、退出码 3,不往上抛。</summary>
    [Fact]
    public void Manager_creation_exception_returns_NoManager()
    {
        _tethering.CreateError = new InvalidOperationException("模拟创建失败");

        var code = Service().Run(Options.Parse(new string[0]));

        Assert.Equal(ExitCode.NoManager, code);
        Assert.Contains("模拟创建失败", _log.Text);
    }

    // ===================================================================
    // 上游信息落地
    // ===================================================================

    /// <summary>连接名与连接等级必须写进日志 —— 判断"为什么手机连上了却上不了网"靠它。</summary>
    [Fact]
    public void Logs_profile_name_and_connectivity_level()
    {
        _connectivity.ProfileName = "PPPoE-宽带";
        _connectivity.ConnectivityLevel = "LocalAccess";

        Service().Run(Options.Parse(new string[0]));

        Assert.Contains("PPPoE-宽带", _log.Text);
        Assert.Contains("LocalAccess", _log.Text);
        Assert.Same(_connectivity.DefaultProfile, _tethering.LastCreatedProfile);
    }

    /// <summary>日志要标明走了 --any 路径,便于事后分辨"这次为什么没等外网"。</summary>
    [Fact]
    public void Logs_any_mode_marker()
    {
        Service().Run(Options.Parse(new[] { "--any" }));

        Assert.Contains("--any 模式", _log.Text);
    }
}
