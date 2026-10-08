using HotspotGo.Cli;
using Xunit;

namespace HotspotGo.Tests.Cli;

/// <summary>
/// 用法说明。
///
/// 本组件是 WinExe,没有控制台 —— 说明文字只能写进 log.txt,
/// 所以它就是用户唯一的"帮助",内容和 README 的用法表是同一份信息。
/// 这里锁两件事:每个开关都被写到,以及退出码表在里面
/// (写计划任务的人不该为了知道 2 / 3 / 4 是什么意思去翻源码)。
/// </summary>
public class UsageTextTests
{
    private static string All => string.Join("\n", UsageText.Lines);

    /// <summary>每个开关都必须出现在说明里 —— 少一个,用户就查不到它。</summary>
    [Theory]
    [InlineData("--wait")]
    [InlineData("--any")]
    [InlineData("--status")]
    [InlineData("--off")]
    [InlineData("--help")]
    [InlineData("--version")]
    public void Usage_documents_every_switch(string flag)
    {
        Assert.Contains(flag, All);
    }

    /// <summary>短写也要写出来(排版是"-s, --status"),否则用户不知道短写能用。</summary>
    [Theory]
    [InlineData("-w")]
    [InlineData("-a")]
    [InlineData("-s")]
    [InlineData("-o")]
    [InlineData("-h")]
    [InlineData("-v")]
    public void Usage_documents_short_switches(string flag)
    {
        Assert.Contains(flag + ",", All);
    }

    /// <summary>退出码表要在说明里,且包含新增的"参数无法识别"。</summary>
    [Fact]
    public void Usage_documents_the_exit_codes()
    {
        Assert.Contains("退出码:", All);
        Assert.Contains("2 没有可用连接", All);
        Assert.Contains("3 拿不到热点管理器", All);
        Assert.Contains("4 开启失败", All);
        Assert.Contains("5 参数无法识别", All);
    }

    /// <summary>
    /// 说明里要把两种拼法的规则讲清楚(长开关两个横线、短写一个横线)。
    ///
    /// 这条尤其要紧:单横线的长写法是<b>不认</b>的,用户敲了 <c>-status</c> 之后
    /// 只能从这段说明里看出为什么 —— 而当初就是有人这么敲的。
    /// </summary>
    [Fact]
    public void Usage_states_how_long_and_short_forms_are_spelled()
    {
        Assert.Contains("两个横线", All);
        Assert.Contains("一个横线", All);
    }

    /// <summary>
    /// 说明的每一行都必须有内容,且不能自带换行 ——
    /// 自带换行会让那几行在 log.txt 里没有时间戳(见 <see cref="UsageText"/> 的注释)。
    /// </summary>
    [Fact]
    public void Usage_lines_are_single_line_and_non_empty()
    {
        Assert.NotEmpty(UsageText.Lines);
        foreach (var line in UsageText.Lines)
        {
            Assert.False(string.IsNullOrWhiteSpace(line), "用法说明里有空行");
            Assert.DoesNotContain("\n", line);
            Assert.DoesNotContain("\r", line);
        }
    }
}
