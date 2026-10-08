using System.Collections.Generic;

namespace HotspotGo.Cli;

/// <summary>
/// 用法说明。<c>--help</c> 时写出,参数无法识别时一并写出
/// (见 <c>Program.Preflight</c>)。
///
/// 为什么是一行一条、而不是一整段多行文本:本组件是 WinExe,没有控制台,
/// log.txt 是唯一出口,而 <c>AppendFileLogger</c> 给每次 WriteLine 加一个时间戳 ——
/// 一次写进一整段多行文本的话,只有第一行有时间戳,后面几行就破坏了
/// "每行都带时间戳"这个约定(冒烟脚本也按它判断日志是否完整)。
///
/// 内容与 README 的用法表是同一份信息,改了这里记得同步 README。
/// </summary>
internal static class UsageText
{
    /// <summary>用法说明的每一行,顺序即输出顺序。</summary>
    public static readonly IReadOnlyList<string> Lines = new[]
    {
        "用法:HotspotGo.exe [选项]",
        "  (无参数)      等电脑连上网后开启热点(最多等 90 秒)",
        "  --wait 秒数   自定义最长等待秒数(0 - 86400,默认 90)",
        "  --any         不等外网,直接用当前可用连接开启热点(电脑没网时也能开)",
        "  --status      查看状态(开关 / Wi-Fi 名 / 已连设备数),不做任何改动",
        "  --off         关闭热点",
        "  --help        显示本说明(-h / -? / /? 同义)",
        "退出码:0 成功 / 1 致命异常 / 2 没有可用连接 / 3 拿不到热点管理器 / 4 开启失败 / 5 参数无法识别",
        "日志:exe 同目录的 log.txt",
    };
}
