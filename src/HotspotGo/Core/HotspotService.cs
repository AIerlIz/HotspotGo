using System;
using HotspotGo.Cli;
using HotspotGo.Logging;

namespace HotspotGo.Core;

/// <summary>
/// 业务编排:等上游就绪 → 创建热点管理器 → 按命令执行(查 / 开 / 关)。
///
/// 流程:
///   <list type="number">
///     <item>按命令决定等待策略,拿到可共享的连接配置(热点的共享源必须是"已联网"的连接)</item>
///     <item>拿到热点管理器</item>
///     <item>读当前状态;若已是目标状态则直接返回</item>
///     <item>否则调 Start/StopTetheringAsync,并确认状态已生效</item>
///   </list>
/// 每一步的结果都落日志;每一步失败对应一个退出码(见 <see cref="ExitCode"/>)。
///
/// 本层不认识 WinRT:所有外部操作走 <see cref="IConnectivityApi"/> /
/// <see cref="ITetheringApi"/>(接口就定义在 Core),由组装根(Program)注入真实实现、
/// 测试注入假实现 —— 所以这里每个岔路口都能在单元测试里复现。
///
/// 这份"能测"的收益来自一条原则:<b>策略住在 Core,通道住在 WinRT</b>。
/// 所以"哪条连接能拿来开热点"和"状态算不算达标"都在本层 / <see cref="HotspotState"/>,
/// 而不是在 WinRT 封装里(那两处以前都在 WinRT 层,是唯一测不到的业务判断)。
/// </summary>
internal sealed class HotspotService
{
    /// <summary>正常模式下等待上游的轮询间隔。</summary>
    private static readonly TimeSpan NormalPollInterval = TimeSpan.FromSeconds(2);

    /// <summary>--off 模式:上游可能已断开,快速尝试后即放弃(约 3 秒)。</summary>
    private static readonly TimeSpan TurnOffTimeout = TimeSpan.FromSeconds(3);

    /// <summary>--off 模式的轮询间隔。</summary>
    private static readonly TimeSpan TurnOffPollInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// --status 模式拿连接配置的上限。
    ///
    /// 只读命令不该让人干等,所以既不套用常规模式的 90 秒,也不去问用户要 --wait:
    /// 拿连接配置是一次同步查询,3 秒还拿不到就是没有(与 --off 同一个量级、同一个理由)。
    /// (没有连接就查不了热点状态 —— 热点 API 必须挂到一条连接上。)
    /// </summary>
    private static readonly TimeSpan StatusTimeout = TimeSpan.FromSeconds(3);

    /// <summary>--status 模式的轮询间隔。</summary>
    private static readonly TimeSpan StatusPollInterval = TimeSpan.FromMilliseconds(500);

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
            LogNoUpstream(options);
            return ExitCode.NoUpstream;
        }

        _log.WriteLine("  拿到 profile: " + ReadSafely(() => _connectivity.ReadProfileName(profile), "连接名"));
        _log.WriteLine("  连接等级: " + ReadSafely(() => _connectivity.ReadConnectivityLevel(profile), "连接等级") +
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

    /// <summary>按命令选择等待策略:关热点要快(上游可能已断),查状态也要快,只有开热点值得等。</summary>
    private object WaitForUpstream(Options options)
    {
        // --any:不等外网。只要有连接(哪怕只能本地通)就用它开热点
        if (options.Command == HotspotCommand.TurnOn && options.UseAnyConnection)
        {
            _log.WriteLine("--any 模式:不等外网,直接使用当前可用连接");
            return SelectShareableProfile();
        }

        var monitor = new UpstreamMonitor(_log, _connectivity);

        if (options.Command == HotspotCommand.TurnOff)
        {
            _log.WriteLine("--off 模式:直接拿当前连接配置");
            return monitor.WaitForInternetProfile(TurnOffTimeout, TurnOffPollInterval);
        }

        if (options.Command == HotspotCommand.Status)
        {
            _log.WriteLine("--status 模式:最多等 " + (int)StatusTimeout.TotalSeconds + " 秒拿当前连接配置");
            return monitor.WaitForInternetProfile(StatusTimeout, StatusPollInterval);
        }

        _log.WriteLine("等待上游连接就绪(最多 " + options.MaxWaitSeconds + " 秒)...");
        return monitor.WaitForInternetProfile(
            TimeSpan.FromSeconds(options.MaxWaitSeconds), NormalPollInterval);
    }

    /// <summary>
    /// --any 用:在系统所有连接里挑一条能开热点的 —— <b>优先能上外网的</b>,
    /// 没有就退而取任意一条已连接的(有本地链路即可,例如网线接了但路由器没通)。
    /// 一条都没有(所有网卡都断开,或连接列表读不出来)时返回 null。
    ///
    /// 这里就是那条挑选规则。它以前住在 WinRT 层(ConnectivityApi.SelectShareableProfile),
    /// 于是成了整份代码里唯一没有被任何测试覆盖的业务决策 —— 偏偏它决定了热点挂到哪条连接上。
    /// 现在接口只回答"有哪些连接 / 这条什么等级",选哪条在这里定,测试里就能完整复现。
    /// </summary>
    private object SelectShareableProfile()
    {
        object fallback = null;

        try
        {
            foreach (object profile in _connectivity.GetAllProfiles())
            {
                string level = ReadSafely(() => _connectivity.ReadConnectivityLevel(profile), "连接等级");

                if (!IsShareable(level))
                {
                    _log.WriteLine("  跳过 " +
                        ReadSafely(() => _connectivity.ReadProfileName(profile), "连接名") +
                        "(等级 " + level + ")");
                    continue;
                }

                if (level == ConnectivityLevel.InternetAccess) return profile;
                if (fallback == null) fallback = profile;
            }
        }
        catch (Exception ex)
        {
            // 枚举连接列表本身失败(典型:这台机器解析不到 WinRT 类型)。
            // 在这里兜住,退出码才是明确的 2(没有可用连接),
            // 而不是逃到顶层变成 1(FATAL + 一屏堆栈,看日志的人还得自己判断这是环境问题)。
            _log.WriteLine("[错误] 枚举连接配置失败: " + Describe(ex));
            return null;
        }

        return fallback;
    }

    /// <summary>"这条连接能不能拿来开热点":读到 None、或者根本读不到等级,都算不能。</summary>
    private static bool IsShareable(string level)
        => !string.IsNullOrEmpty(level) &&
           level != ConnectivityLevel.None &&
           level != UnknownValue.Placeholder;

    /// <summary>拿不到上游时按命令给出对应的原因 —— 三种情况该说的话不一样,别用一句"超时"糊过去。</summary>
    private void LogNoUpstream(Options options)
    {
        switch (options.Command)
        {
            case HotspotCommand.TurnOff:
                _log.WriteLine("[失败] --off 模式下也拿不到 Internet 连接配置。热点可能本来就没开。");
                break;

            case HotspotCommand.Status:
                _log.WriteLine("[失败] --status 模式下也拿不到 Internet 连接配置 —— " +
                    "查询热点状态也得先挂到一条连接上。");
                break;

            default:
                if (options.UseAnyConnection)
                {
                    _log.WriteLine("[失败] 系统里没有任何可用的连接 —— 热点必须挂到一条连接上。");
                    _log.WriteLine("       (先接上网线 / 连上 Wi-Fi,哪怕它暂时出不了外网;" +
                        "若上面还有 [错误] 行,那是读连接列表本身失败了。)");
                }
                else
                {
                    _log.WriteLine("[失败] 超时:始终没有 Internet 连接配置文件。");
                    _log.WriteLine("       (拨号可能还没连上,本次放弃。下次登录会重试。)");
                }
                break;
        }
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
            _log.WriteLine("[错误] CreateFromConnectionProfile: " + Describe(ex));
            return null;
        }
    }

    /// <summary>打印当前热点状态、SSID、客户端数,并返回状态文本供后续判断。</summary>
    private string ReportCurrentState(object manager)
    {
        string state = ReadSafely(() => _tethering.ReadState(manager), "热点状态");
        _log.WriteLine("当前热点状态: " + state);
        _log.WriteLine("  SSID: " + ReadSafely(() => _tethering.ReadSsid(manager), "SSID"));
        _log.WriteLine("  客户端数: " + ReadSafely(() => _tethering.ReadClientCount(manager), "客户端数") +
            " / " + ReadSafely(() => _tethering.ReadMaxClientCount(manager), "最大客户端数"));
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
            _log.WriteLine(currentState == UnknownValue.Placeholder
                ? "读不到热点状态(读到的是占位值),按未开启处理,不做任何操作。"
                : "热点本来就没开,无需操作。");
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
                " 客户端=" + ReadSafely(() => _tethering.ReadClientCount(manager), "客户端数"));
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
            _log.WriteLine("[错误] 开/关热点异常: " + Describe(ex));

            // 读状态本身也可能抛(例如管理器对象在等待期间失效),所以这里不能直接调 ReadState ——
            // 那会让"已经决定要折叠异常"的这一步反而抛出异常。
            return ToggleResult.Failure(
                ReadSafely(() => _tethering.ReadState(manager), "热点状态"), Describe(ex));
        }
    }

    /// <summary>
    /// 读一个字符串值;读失败只记日志并返回占位文本,绝不抛出。
    ///
    /// 接口契约没保证实现不会抛(真机上 COM 对象可能已失效),而下面每个调用点都在
    /// "日志写不下来就查不出问题"的路径上 —— 一次读失败不该把整轮流程掀翻成 FATAL,
    /// 那会让 log.txt 里只剩堆栈、没有上下文。
    /// </summary>
    /// <param name="read">真正的读取动作。</param>
    /// <param name="what">读的是什么(拼进错误行)。</param>
    private string ReadSafely(Func<string> read, string what)
    {
        try
        {
            return read();
        }
        catch (Exception ex)
        {
            _log.WriteLine("[错误] 读" + what + "失败: " + Describe(ex));
            return UnknownValue.Placeholder;
        }
    }

    /// <summary>异常写成一行,带类型名 —— 只留 Message 的话,不同故障看起来会一模一样。</summary>
    private static string Describe(Exception ex) => ex.GetType().Name + ": " + ex.Message;
}
