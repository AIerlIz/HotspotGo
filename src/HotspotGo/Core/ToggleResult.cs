using System;

namespace HotspotGo.Core;

/// <summary>
/// 一次开 / 关热点操作的结果。
///
/// 放在 Core 而不是 WinRT 层:它是给业务判断用的结果对象
/// (成功与否 → 退出码,<see cref="Describe"/> → 写进 log.txt 的那一行),
/// 不是 WinRT 的概念。WinRT 侧的 <c>TetheringApi.ToggleAndWait</c> 反过来构造它。
/// </summary>
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
