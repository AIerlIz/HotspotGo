using System;

namespace HotspotGo.Core;

/// <summary>
/// 把异常写成日志里那一行。
///
/// 为什么不能只写 <c>ex.GetType().Name + ": " + ex.Message</c>:本程序调 WinRT 全靠反射,
/// 而 <see cref="System.Reflection.MethodInfo.Invoke(object, object[])"/> 会把目标抛出的异常
/// <b>包一层</b> <see cref="System.Reflection.TargetInvocationException"/>,那一层的 Message 是
/// 固定的"调用的目标发生了异常",真正的原因(例如 <c>异常来自 HRESULT: 0x83120001</c>)
/// 留在 InnerException 里。
///
/// 这不是纸上推理,是真机踩出来的:一次故障里日志只留下
/// <c>CreateFromConnectionProfile: TargetInvocationException: 调用的目标发生了异常。</c>
/// —— 等于什么都没说,还得手工把同一调用再挖一遍才知道操作系统给的是什么。
/// 所以这里一路走到最深的那层(<see cref="Exception.GetBaseException"/>)。
///
/// 为什么不额外追加 HResult:普通托管异常的 HResult 全是样板值
/// (InvalidOperationException 是 0x80131509、ArgumentException 是 0x80070057),
/// 追加只会给日志里每一行都添一串没信息量的码;而真正有信息量的那类码(WinRT / Win32)
/// 本来就在消息正文里("异常来自 HRESULT: 0x83120001")—— 解开反射包装就看得见。
/// 这条取舍由 <c>ExceptionTextTests</c> 钉着。
///
/// 输出保证是<b>完整的一行</b> —— 日志每行都要带时间戳(见 <c>UsageText</c> 里的说明),
/// 而异常消息偶尔自带换行,这里统一压掉。
/// </summary>
internal static class ExceptionText
{
    /// <summary>把异常写成一行:最深那层的类型名 + 消息。</summary>
    public static string Describe(Exception exception)
    {
        Exception root = exception.GetBaseException();

        return (root.GetType().Name + ": " + root.Message)
            .Replace("\r\n", " ")
            .Replace('\r', ' ')
            .Replace('\n', ' ');
    }
}
