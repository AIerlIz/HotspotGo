using HotspotGo.Cli;
using Xunit;

namespace HotspotGo.Tests.Cli;

/// <summary>
/// "这次有没有人在看终端、要不要藏窗口"的判定。
///
/// 之所以值得单独钉死:这条规则错一半的代价很不对称 ——
///   - 该藏没藏:双击 / 开机自启时弹一个黑窗(这台工具最初的体验问题);
///   - 不该藏却藏了:<b>把用户自己正在用的终端窗口弄没了</b>。
/// 而且它只能靠真机观察,所以判别规则被拆成了纯函数 <see cref="Terminal.Plan"/>,
/// 连"进程数取不到"这种只在异常路径上出现的分支也一并覆盖;
/// 真正动手那一小段(内核调用)留给真机验证。
/// </summary>
public class TerminalTests
{
    /// <summary>
    /// 六种组合逐一钉死。
    ///
    /// ownership 参数声明成 object 只是为了绕过可访问性:ConsoleOwnership 是 internal
    /// (整个程序集的类型都是 internal,见 csproj),而 xunit 的用例方法必须是 public,
    /// public 方法不能带 internal 类型的参数。
    /// </summary>
    [Theory]
    // 输出被接走(重定向到文件 / 管道):照常写,与有没有窗口无关
    [InlineData(true, false, 0u, true, false)]
    [InlineData(true, true, 1u, true, true)]
    [InlineData(true, true, 3u, true, false)]
    // 没有人在接输出:只有"从终端继承来的控制台"才该写 —— 那是人在看
    [InlineData(false, false, 0u, false, false)]
    [InlineData(false, true, 1u, false, true)]
    [InlineData(false, true, 3u, true, false)]
    public void Plan_covers_every_combination(
        bool stdoutIsCaptured, bool hasConsoleWindow, uint attachedProcessCount,
        bool expectWrite, bool expectHide)
    {
        var plan = Terminal.Plan(stdoutIsCaptured, hasConsoleWindow, attachedProcessCount);

        Assert.Equal(expectWrite, plan.WriteToTerminal);
        Assert.Equal(expectHide, plan.HideWindow);
    }

    /// <summary>
    /// 最要紧的一条:从 cmd / PowerShell 继承来的控制台<b>永远不许藏</b>。
    /// 藏了就是把用户自己的终端窗口弄没 —— 比弹黑窗严重得多。
    /// </summary>
    [Fact]
    public void Inherited_console_is_never_hidden()
    {
        Assert.False(Terminal.Plan(false, hasConsoleWindow: true, attachedProcessCount: 2).HideWindow);
        Assert.False(Terminal.Plan(true, hasConsoleWindow: true, attachedProcessCount: 2).HideWindow);
    }

    /// <summary>
    /// 进程数<b>取不到</b>时(<c>GetConsoleProcessList</c> 失败会返回 0)必须当作"继承来的、别动"。
    ///
    /// 这是这套代码里唯一"判错就伤人"的分支:若把 0 也当成"只有我们自己",就会去藏窗口 ——
    /// 而在真的继承了父控制台的情况下,藏掉的是用户自己正在用的那个终端窗口。
    /// 宁可让本该藏的窗口留着(顶多闪一下),也不能赌。
    /// </summary>
    [Fact]
    public void Unreadable_process_count_never_hides_the_window()
    {
        var plan = Terminal.Plan(stdoutIsCaptured: false, hasConsoleWindow: true, attachedProcessCount: 0);

        Assert.Equal(ConsoleOwnership.Inherited, plan.Ownership);
        Assert.False(plan.HideWindow);
        Assert.True(plan.WriteToTerminal);
    }

    /// <summary>
    /// 反过来:系统为本次启动新建的控制台一律藏掉,而且这时不该再往终端写 ——
    /// 那正是双击 / 启动文件夹 / 计划任务的样子:不打扰,记录留给 log.txt。
    /// </summary>
    [Fact]
    public void Newly_created_console_is_hidden_and_left_silent()
    {
        var plan = Terminal.Plan(false, hasConsoleWindow: true, attachedProcessCount: 1);

        Assert.True(plan.HideWindow);
        Assert.False(plan.WriteToTerminal);
    }

    /// <summary>
    /// 没有控制台但输出被重定向时仍要写:计划任务把输出重定向进一个文件是完全正常的用法,
    /// 不能因为"没有窗口"就把这路输出一起丢掉。
    /// </summary>
    [Fact]
    public void Captured_output_is_written_even_without_a_console()
    {
        var plan = Terminal.Plan(true, hasConsoleWindow: false, attachedProcessCount: 0);

        Assert.True(plan.WriteToTerminal);
        Assert.False(plan.HideWindow);
    }

    // ===================================================================
    // 写进 log.txt 的那句话
    // ===================================================================

    private static TerminalSetup Setup(
        bool captured, bool hasConsoleWindow, uint attachedProcessCount, bool windowHidden = false)
        => new TerminalSetup(
            Terminal.Plan(captured, hasConsoleWindow, attachedProcessCount), windowHidden);

    /// <summary>
    /// 这句话是"双击 / 开机自启时为什么一点反应都没有"的唯一答案,所以逐种情况锁住 ——
    /// 措辞不准,查日志的人就会被带偏。
    /// </summary>
    [Fact]
    public void Describe_says_it_inherited_the_console()
    {
        Assert.Contains("继承", Setup(captured: false, hasConsoleWindow: true, attachedProcessCount: 3).Describe());
    }

    /// <summary>双击那种情况要明确说"已隐藏" —— 否则"没有输出"看起来像故障。</summary>
    [Fact]
    public void Describe_reports_the_console_was_hidden()
    {
        Assert.Contains("已隐藏",
            Setup(captured: false, hasConsoleWindow: true, attachedProcessCount: 1, windowHidden: true).Describe());
    }

    /// <summary>
    /// 藏失败时不许谎报"已隐藏":日志是唯一排障入口,说一句没做到的话
    /// 会让查日志的人往错的方向找(所以这句来自 ShowWindow 之后的实际复核)。
    /// </summary>
    [Fact]
    public void Describe_does_not_claim_a_hide_that_failed()
    {
        var text = Setup(captured: false, hasConsoleWindow: true, attachedProcessCount: 1,
            windowHidden: false).Describe();

        Assert.DoesNotContain("已隐藏", text);
        Assert.Contains("没能藏掉", text);
    }

    /// <summary>重定向 / 管道时要说清输出去哪儿了。</summary>
    [Fact]
    public void Describe_reports_captured_output()
    {
        Assert.Contains("重定向", Setup(captured: true, hasConsoleWindow: true, attachedProcessCount: 3).Describe());
    }

    /// <summary>判断失败要如实说明,并且说明按什么处理的。</summary>
    [Fact]
    public void Describe_reports_a_failed_detection()
    {
        var unknown = new TerminalSetup(
            new TerminalPlan(false, false, ConsoleOwnership.Unknown, false), false);

        Assert.Contains("出错", unknown.Describe());
        Assert.Contains("log.txt", unknown.Describe());
    }
}
