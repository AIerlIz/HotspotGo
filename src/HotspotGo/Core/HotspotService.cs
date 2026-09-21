using System;
using HotspotGo.Cli;
using HotspotGo.Logging;

namespace HotspotGo.Core;

/// <summary>
/// 业务编排:等上游就绪 → 创建热点管理器 → 按命令执行(查 / 开 / 关)。
///
/// 流程:
///   <list type="number">
///     <item>轮询等待拨号连接就绪(热点的共享源必须是"已联网"的连接)</item>
///     <item>拿到热点管理器</item>
///     <item>读当前状态;若已是目标状态则直接返回</item>
///     <item>否则调 Start/StopTetheringAsync,并确认状态已生效</item>
///   </list>
/// 每一步的结果都落日志;每一步失败对应一个退出码(见 <see cref="ExitCode"/>)。
///
/// 本层不认识 WinRT:所有外部操作走 <see cref="IConnectivityApi"/> /
/// <see cref="ITetheringApi"/>(接口就定义在 Core),由组装根(Program)注入真实实现、
/// 测试注入假实现 —— 所以这里每个岔路口都能在单元测试里复现。
/// </summary>
internal sealed class HotspotService
{
    /// <summary>正常模式下等待上游的轮询间隔。</summary>
    private static readonly TimeSpan NormalPollInterval = TimeSpan.FromSeconds(2);

    /// <summary>--off 模式:上游可能已断开,快速尝试后即放弃(约 3 秒)。</summary>
    private static readonly TimeSpan TurnOffTimeout = TimeSpan.FromSeconds(3);

    /// <summary>--off 模式的轮询间隔。</summary>
    private static readonly TimeSpan TurnOffPollInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>等待热点状态达到目标(开/关生效)的上限。</summary>
    private static readonly TimeSpan ToggleTimeout = TimeSpan.FromSeconds(30);

    private readonly ILogger _log;
    private readonly IConnectivityApi _connectivity;
    private readonly ITetheringApi _tethering;

    /// <summary>依赖全部由组装根注入:生产路径见 <see cref="Program"/>,测试路径见 tests/Core。</summary>
    public HotspotService(ILogger log, IConnectivityApi connectivity, ITetheringApi tethering)
    {
        _log = log;
        _connectivity = connectivity;
        _tethering = tethering;
    }

    /// <summary>执行一次完整流程,返回进程退出码。</summary>
    public ExitCode Run(Options options)
    {
        object profile = WaitForUpstream(options);
        if (profile == null)
        {
            _log.WriteLine(options.UseAnyConnection && options.Command == HotspotCommand.TurnOn
                ? "[失败] 系统里没有任何已连接的网卡 —— 热点必须挂到一条连接上。\n       (先接上网线 / 连上 Wi-Fi,哪怕它暂时出不了外网。)"
                : options.Command == HotspotCommand.TurnOff
                    ? "[失败] --off 模式下也拿不到 Internet 连接配置。热点可能本来就没开。"
                    : "[失败] 超时:始终没有 Internet 连接配置文件。\n       (拨号可能还没连上,本次放弃。下次登录会重试。)");
            return ExitCode.NoUpstream;
        }

        _log.WriteLine("  拿到 profile: " + _connectivity.ReadProfileName(profile));
        _log.WriteLine("  连接等级: " + _connectivity.ReadConnectivityLevel(profile) +
            "(不是 InternetAccess 时,连上热点的设备只能互访,出不了外网)");

        object manager = TryCreateManager(profile);
        if (manager == null)
        {
            _log.WriteLine("[失败] CreateFromConnectionProfile 返回 null —— 无法管理热点。");
            return ExitCode.NoManager;
        }

        string state = ReportCurrentState(manager);

        switch (options.Command)
        {
            case HotspotCommand.Status:
                _log.WriteLine("(--status 模式,不做任何改动)");
                return ExitCode.Ok;

            case HotspotCommand.TurnOff:
                return TurnOff(manager, state);

            default:
                return TurnOn(manager, state);
        }
    }

    // ===================================================================
    // 等上游(拨号)就绪
    // ===================================================================

    /// <summary>按命令选择等待策略:关热点要快(上游可能已断),其余等到超时为止。</summary>
    private object WaitForUpstream(Options options)
    {
        // --any:不等外网。只要有连接(哪怕只能本地通)就用它开热点
        if (options.UseAnyConnection && options.Command == HotspotCommand.TurnOn)
        {
            _log.WriteLine("--any 模式:不等外网,直接使用当前可用连接");
            return _connectivity.SelectShareableProfile();
        }

        var monitor = new UpstreamMonitor(_log, _connectivity);

        if (options.Command == HotspotCommand.TurnOff)
        {
            _log.WriteLine("--off 模式:直接拿当前连接配置");
            return monitor.WaitForInternetProfile(TurnOffTimeout, TurnOffPollInterval);
        }

        _log.WriteLine("等待上游连接就绪(最多 " + options.MaxWaitSeconds + " 秒)...");
        return monitor.WaitForInternetProfile(
            TimeSpan.FromSeconds(options.MaxWaitSeconds), NormalPollInterval);
    }

    /// <summary>创建热点管理器;失败时写日志返回 null。</summary>
    private object TryCreateManager(object profile)
    {
        try
        {
            if (!_tethering.IsManagerTypeAvailable())
            {
                _log.WriteLine("[错误] 找不到 TetheringManager 类型");
                return null;
            }

            return _tethering.CreateManager(profile);
        }
        catch (Exception ex)
        {
            _log.WriteLine("[错误] CreateFromConnectionProfile: " + ex.Message);
            return null;
        }
    }

    /// <summary>打印当前热点状态、SSID、客户端数,并返回状态文本供后续判断。</summary>
    private string ReportCurrentState(object manager)
    {
        string state = _tethering.ReadState(manager);
        _log.WriteLine("当前热点状态: " + state);
        _log.WriteLine("  SSID: " + _tethering.ReadSsid(manager));
        _log.WriteLine("  客户端数: " + _tethering.ReadClientCount(manager) +
            " / " + _tethering.ReadMaxClientCount(manager));
        return state;
    }

    // ===================================================================
    // 开 / 关
    // ===================================================================

    /// <summary>关闭热点(本来就没开则直接返回)。关闭失败也返回 Ok,结果见日志。</summary>
    private ExitCode TurnOff(object manager, string currentState)
    {
        if (currentState != HotspotState.On)
        {
            _log.WriteLine("热点本来就没开,无需操作。");
            return ExitCode.Ok;
        }

        _log.WriteLine("正在关闭热点...");
        var result = ToggleSafely(manager, start: false);
        _log.WriteLine("关闭结果: " + result.Describe());
        return ExitCode.Ok;
    }

    /// <summary>开启热点(已经是开状态则直接返回);失败返回 <see cref="ExitCode.StartFailed"/>。</summary>
    private ExitCode TurnOn(object manager, string currentState)
    {
        if (currentState == HotspotState.On)
        {
            _log.WriteLine("[跳过] 热点已经是开启状态,无需操作。");
            return ExitCode.Ok;
        }

        _log.WriteLine("正在开启热点...");
        var result = ToggleSafely(manager, start: true);
        _log.WriteLine("开启结果: " + result.Describe());

        if (result.Succeeded)
        {
            _log.WriteLine("[成功] 热点已开启。状态=" + result.StateAfter +
                " 客户端=" + _tethering.ReadClientCount(manager));
            return ExitCode.Ok;
        }

        _log.WriteLine("[失败] 开启热点未成功。");
        return ExitCode.StartFailed;
    }

    /// <summary>开 / 关热点;异常折叠成一次失败结果,不把异常抛给主流程。</summary>
    private ToggleResult ToggleSafely(object manager, bool start)
    {
        try
        {
            return _tethering.ToggleAndWait(manager, start, ToggleTimeout);
        }
        catch (Exception ex)
        {
            _log.WriteLine("[错误] 开/关热点异常: " + ex.Message);
            return ToggleResult.Failure(_tethering.ReadState(manager), ex.Message);
        }
    }
}
