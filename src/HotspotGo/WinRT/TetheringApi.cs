using System;
using System.Diagnostics;
using System.Threading;

namespace HotspotGo.WinRT;

/// <summary>
/// <c>Windows.Networking.NetworkOperators.NetworkOperatorTetheringManager</c> 的反射封装
/// —— 热点管理器类型名的<b>唯一出处</b>。
///
/// 关于类型名的拼法(.NET Framework 特有,也是本项目能甩掉 24 MB 投影的原因):
///   CLR 内建 WinRT 互操作,按
///   <c>"类型全名, WinRT 程序集名, ContentType=WindowsRuntime"</c>
///   就能从操作系统解析出类型。本类型隶属 WinRT 程序集 <c>Windows.Networking</c>。
///
/// 错误约定:类型不可用时抛异常,由调用方决定怎么记录;
/// <see cref="CreateManager"/> 返回 null 属于业务语义 ——"该上游不能作为热点共享源"。
/// </summary>
internal static class TetheringApi
{
    /// <summary>热点管理器类型的全名(不含限定后缀,报错信息用)。</summary>
    public const string TypeFullName =
        "Windows.Networking.NetworkOperators.NetworkOperatorTetheringManager";

    /// <summary>热点管理器类型的限定名(可直接交给反射解析)。</summary>
    public const string TypeName =
        TypeFullName + ", Windows.Networking, ContentType=WindowsRuntime";

    /// <summary>TetheringOperationalState 取值:已开启。</summary>
    public const string StateOn = "On";

    /// <summary>轮询热点状态、确认开/关已生效的间隔。</summary>
    private static readonly TimeSpan StatePollInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// 用连接配置创建热点管理器 —— 这是"该上游能否做热点共享源"的核心判定。
    /// 返回 null 表示该上游不能作为共享源。
    /// </summary>
    public static object CreateManager(object profile)
    {
        if (profile == null) return null;

        var type = WinrtReflection.FindType(TypeName);
        if (type == null) throw new TypeLoadException("找不到 WinRT 类型: " + TypeFullName);

        return WinrtReflection.InvokeStatic(type, "CreateFromConnectionProfile", new[] { profile });
    }

    /// <summary>读热点运行状态文本(Off / On / ...);读不到返回 "(null)"。</summary>
    public static string ReadState(object manager)
        => WinrtReflection.GetPropertyString(manager, "TetheringOperationalState");

    /// <summary>读当前 SSID;读不到返回 "(null)"。</summary>
    public static string ReadSsid(object manager)
        => WinrtReflection.GetPropertyString(
            WinrtReflection.SafeInvoke(manager, "GetCurrentAccessPointConfiguration"), "Ssid");

    /// <summary>读当前已连接客户端数;读不到返回 "(null)"。</summary>
    public static string ReadClientCount(object manager)
        => WinrtReflection.GetPropertyString(manager, "ClientCount");

    /// <summary>读最大客户端数;读不到返回 "(null)"。</summary>
    public static string ReadMaxClientCount(object manager)
        => WinrtReflection.GetPropertyString(manager, "MaxClientCount");

    /// <summary>
    /// 开 / 关热点,并<b>等到实际状态变成目标值</b>才返回。
    ///
    /// 为什么不解释 Start/StopTetheringAsync 的返回值:
    ///   .NET Framework 上用反射调用返回 WinRT 异步方法时,拿到的是裸
    ///   <c>System.__ComObject</c>,而不是投影过的 <c>IAsyncOperation&lt;T&gt;</c> ——
    ///   没有 Status / Results / GetResults(),解析不出 Success 之类的状态。
    ///   (已实测确认,另见 <see cref="WinrtReflection"/> 里关于 AwaitAsyncOperation
    ///   为何被删除的说明。)
    ///
    ///   所以这里改为"发起调用 → 轮询 TetheringOperationalState 直到达标"。
    ///   对"热点到底开没开成"这个问题,读实际状态比读操作返回值更直接、更可靠。
    /// </summary>
    /// <param name="manager">热点管理器。</param>
    /// <param name="start">true = 开启;false = 关闭。</param>
    /// <param name="timeout">等待状态达标的上限。</param>
    public static ToggleResult ToggleAndWait(object manager, bool start, TimeSpan timeout)
    {
        string stateBefore = ReadState(manager);

        // 异步操作对象要一直持有到轮询结束:丢掉引用虽然不会取消 WinRT 侧的
        // 操作,但持有它更稳妥。
        object asyncOperation = WinrtReflection.SafeInvoke(
            manager, start ? "StartTetheringAsync" : "StopTetheringAsync");

        if (asyncOperation == null)
            return ToggleResult.Failure(stateBefore,
                "调用 " + (start ? "StartTetheringAsync" : "StopTetheringAsync") + " 失败(返回 null)");

        var stopwatch = Stopwatch.StartNew();
        string state = stateBefore;

        while (stopwatch.Elapsed < timeout)
        {
            state = ReadState(manager);
            if (IsTargetReached(state, start))
                return ToggleResult.Success(stateBefore, state, stopwatch.Elapsed);

            Thread.Sleep(StatePollInterval);
        }

        // 超时:再读最后一次,把看到的真实状态报出来
        state = ReadState(manager);
        return ToggleResult.Failure(state,
            "等待 " + (int)timeout.TotalMilliseconds + " 毫秒后状态仍未达标(期望 " + (start ? StateOn : "非 " + StateOn) +
            ",实际 " + state + ")");
    }

    /// <summary>
    /// 判断是否已达到目标:开 → 状态为 On;关 → 状态不再是 On。
    /// (关闭时用"不再 On"而不是"等于 Off",避免 Unavailable 之类的中间态导致误判。)
    /// </summary>
    private static bool IsTargetReached(string state, bool start)
        => start ? state == StateOn : state != StateOn;

    /// <summary>一次开 / 关操作的结果。</summary>
    internal sealed class ToggleResult
    {
        /// <summary>实际状态是否已达标。</summary>
        public bool Succeeded { get; private set; }

        /// <summary>操作前的状态。</summary>
        public string StateBefore { get; private set; }

        /// <summary>操作后(或超时时)读到的状态。</summary>
        public string StateAfter { get; private set; }

        /// <summary>失败原因;成功时为 null。</summary>
        public string Error { get; private set; }

        /// <summary>从发起到状态达标耗时;失败时为 null。</summary>
        public TimeSpan? Elapsed { get; private set; }

        public static ToggleResult Success(string before, string after, TimeSpan elapsed)
            => new ToggleResult { Succeeded = true, StateBefore = before, StateAfter = after, Elapsed = elapsed };

        public static ToggleResult Failure(string after, string error)
            => new ToggleResult { Succeeded = false, StateBefore = null, StateAfter = after, Error = error };

        /// <summary>一行可读文本,写进 log.txt。</summary>
        public string Describe()
            => Succeeded
                ? "成功(状态 " + StateBefore + " → " + StateAfter + ",耗时 " + (int)Elapsed.Value.TotalMilliseconds + " 毫秒)"
                : "失败: " + Error;
    }
}
