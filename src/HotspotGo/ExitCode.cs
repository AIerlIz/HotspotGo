namespace HotspotGo;

/// <summary>退出码:脚本 / 计划任务据此判断结果。</summary>
internal enum ExitCode
{
    /// <summary>成功,或已经是目标状态。</summary>
    Ok = 0,

    /// <summary>致命异常(见 log.txt 的 [FATAL] 行)。</summary>
    Fatal = 1,

    /// <summary>没有可用于共享的连接(等不到外网,或系统里没有任何已连接的网卡)。</summary>
    NoUpstream = 2,

    /// <summary>拿不到热点管理器(上游不能被共享,或系统组件异常)。</summary>
    NoManager = 3,

    /// <summary>开启热点失败。</summary>
    StartFailed = 4,
}
