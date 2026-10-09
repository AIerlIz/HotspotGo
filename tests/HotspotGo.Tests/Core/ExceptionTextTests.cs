using System;
using System.Reflection;
using HotspotGo.Core;
using Xunit;

namespace HotspotGo.Tests.Core;

/// <summary>
/// 异常 → 日志里那一行。
///
/// 重点是<b>反射包装</b>:本程序调 WinRT 全靠反射,而 <c>MethodInfo.Invoke</c> 会把真原因包进
/// <c>TargetInvocationException</c>,那一层的 Message 是固定的"调用的目标发生了异常"。
/// 真机上就因为只读最外层,日志里把 OS 给的原因整段丢了 —— 所以这里不但要测手工构造的包装,
/// 还要用<b>真反射</b>抛一次,确认前提成立、并且确实挖到了最里面。
/// </summary>
public class ExceptionTextTests
{
    /// <summary>普通异常:类型名 + 消息(与老写法一致,调用方原有的断言都还成立)。</summary>
    [Fact]
    public void Describes_a_plain_exception()
    {
        Assert.Equal("InvalidOperationException: 模拟失败",
            ExceptionText.Describe(new InvalidOperationException("模拟失败")));
    }

    /// <summary>
    /// 真反射抛出来的异常必须挖到最里面那层。
    ///
    /// 顺带锁住前提:反射<b>确实</b>会包一层 —— 哪天这个行为变了,这条用例会先失败,
    /// 而不是让"日志里只剩一句没用的话"悄悄回来。
    /// </summary>
    [Fact]
    public void Unwraps_a_real_reflection_wrapper()
    {
        var method = typeof(ExceptionTextTests)
            .GetMethod(nameof(ThrowFromReflection), BindingFlags.NonPublic | BindingFlags.Static);

        var captured = Record.Exception(() => method.Invoke(null, null));

        Assert.IsType<TargetInvocationException>(captured);

        Assert.Equal("InvalidOperationException: 反射里的真原因", ExceptionText.Describe(captured));
    }

    /// <summary>
    /// 真机那次故障的原始形状:反射包装 + WinRT 的 HRESULT 消息。
    /// 解开之后那个码要原样出现在日志行里 —— 那正是当时丢掉、还得手工再挖一遍的东西。
    /// </summary>
    [Fact]
    public void Keeps_the_hresult_that_comes_inside_the_wrapped_message()
    {
        var wrapped = new TargetInvocationException(new Exception("异常来自 HRESULT: 0x83120001"));

        Assert.Equal("Exception: 异常来自 HRESULT: 0x83120001", ExceptionText.Describe(wrapped));
    }

    /// <summary>套了好几层时也要走到最深那层。</summary>
    [Fact]
    public void Unwraps_deeply_nested_exceptions()
    {
        var deep = new Exception("最外层", new InvalidOperationException("中间", new ArgumentException("最内")));

        Assert.Equal("ArgumentException: 最内", ExceptionText.Describe(deep));
    }

    /// <summary>
    /// 不追加 HResult:普通托管异常的 HResult 全是样板值
    /// (InvalidOperationException 是 0x80131509),加上去只会给日志每一行都添噪声。
    /// 这条是写完第一版就被测试打回来的 —— 当时它给每个普通异常都补了一串没用的码。
    /// </summary>
    [Fact]
    public void Does_not_add_the_boilerplate_hresult_of_managed_exceptions()
    {
        Assert.DoesNotContain("0x", ExceptionText.Describe(new InvalidOperationException("模拟失败")));
        Assert.DoesNotContain("0x", ExceptionText.Describe(new ArgumentException("模拟失败")));
    }

    /// <summary>消息里自带换行时压成一行 —— 日志每行都得带时间戳。</summary>
    [Fact]
    public void Collapses_newlines_into_one_line()
    {
        string text = ExceptionText.Describe(new InvalidOperationException("第一行\r\n第二行\n第三行"));

        Assert.DoesNotContain("\n", text);
        Assert.DoesNotContain("\r", text);
        Assert.Contains("第一行", text);
        Assert.Contains("第三行", text);
    }

    /// <summary>反射调用的目标:故意抛一个,给上面那条用例用。</summary>
    private static void ThrowFromReflection() => throw new InvalidOperationException("反射里的真原因");
}
