using System;
using System.Runtime.InteropServices;

namespace HotspotGo.Cli;

/// <summary>本进程的控制台是哪来的。</summary>
internal enum ConsoleOwnership
{
    /// <summary>判断失败(取事实的过程出错)。按"没人看"处理,而且绝不去动窗口。</summary>
    Unknown,

    /// <summary>根本没有控制台(以 DETACHED_PROCESS 之类的方式启动)。</summary>
    Missing,

    /// <summary>控制台上只挂着我们自己 —— 是系统为这次启动新建的(双击 / 启动文件夹 / 计划任务)。</summary>
    NewForUs,

    /// <summary>控制台上还挂着别的进程(cmd / PowerShell 等父进程),或者数不清(见 <see cref="Terminal.Plan"/>)—— 都按继承处理。</summary>
    Inherited,
}

/// <summary>
/// 纯判定的结果:该不该写终端、该不该藏窗口。只看事实,不碰任何系统状态 ——
/// 所以它能被单元测试逐格覆盖,而"动手"那部分留给 <see cref="Terminal.Prepare"/>。
/// </summary>
internal readonly struct TerminalPlan
{
    public TerminalPlan(bool writeToTerminal, bool hideWindow, ConsoleOwnership ownership, bool stdoutIsCaptured)
    {
        WriteToTerminal = writeToTerminal;
        HideWindow = hideWindow;
        Ownership = ownership;
        StdoutIsCaptured = stdoutIsCaptured;
    }

    /// <summary>是否把日志同时写到终端(给人看的那一份)。</summary>
    public bool WriteToTerminal { get; }

    /// <summary>是否该把控制台窗口藏起来。</summary>
    public bool HideWindow { get; }

    /// <summary>控制台的来历(用于解释"输出为什么在 / 不在终端")。</summary>
    public ConsoleOwnership Ownership { get; }

    /// <summary>标准输出是否指向文件 / 管道。</summary>
    public bool StdoutIsCaptured { get; }
}

/// <summary>
/// 这次启动实际做了什么:判定 + 隐藏窗口的<b>真实结果</b>。
///
/// 为什么要把"结果"和"判定"分开:判定是纯的(可测),而藏窗口能不能成只有真机知道。
/// 日志里那句"已隐藏"必须来自实际结果 —— 它是唯一排障入口,说一句没做到的话
/// 会让查日志的人往错的方向找(见 <see cref="Describe"/> 里那条"没能藏掉")。
/// </summary>
internal readonly struct TerminalSetup
{
    public TerminalSetup(TerminalPlan plan, bool windowHidden)
    {
        Plan = plan;
        WindowHidden = windowHidden;
    }

    public TerminalPlan Plan { get; }

    /// <summary>窗口是否真的藏掉了(藏完用 IsWindowVisible 复核过)。</summary>
    public bool WindowHidden { get; }

    /// <summary>是否把日志同时写到终端。</summary>
    public bool WriteToTerminal => Plan.WriteToTerminal;

    /// <summary>
    /// 把这次的处理写成一句话,给 log.txt 用。
    ///
    /// 这句话是"双击 / 开机自启时为什么一点反应都没有"的唯一答案,所以刻意说得直白,
    /// 而且只说真发生的事(藏窗口没成就不说"已隐藏")。
    /// </summary>
    public string Describe()
    {
        if (Plan.Ownership == ConsoleOwnership.Unknown) return "判断终端时出错,按只写 log.txt 处理";

        if (!Plan.WriteToTerminal)
        {
            if (Plan.Ownership != ConsoleOwnership.NewForUs) return "没有控制台,只写 log.txt";

            return WindowHidden
                ? "系统新建的控制台已隐藏,只写 log.txt"
                : "系统新建的控制台窗口没能藏掉,只写 log.txt";
        }

        return Plan.StdoutIsCaptured ? "输出被重定向到文件 / 管道" : "从终端继承的控制台";
    }
}

/// <summary>
/// 这次运行"有没有人在看终端",以及系统给我们新建的控制台窗口要不要藏起来。
///
/// 背景:本程序是<b>控制台子系统</b>(见 csproj 里的说明)—— 这样从 cmd / PowerShell 里跑时
/// shell 会等它跑完,输出、管道、重定向、退出码才都成立(WinExe 下 shell 不等 GUI 子系统进程,
/// 输出会落在提示符之后,$LASTEXITCODE 也拿不到)。
/// 代价是双击 / 启动文件夹那种"没有父控制台"的启动方式下,Windows 会给它新建一个控制台窗口
/// —— 那正是要避免的(开机自启时弹一个黑窗)。所以启动第一件事就是判断这个控制台是哪来的:
///   <list type="bullet">
///     <item>控制台上只挂着我们自己 → 是系统新建的 → <b>藏掉窗口</b>,恢复"不打扰"。</item>
///     <item>控制台上还挂着父进程(cmd / PowerShell)→ 是继承来的 → <b>绝不能动</b>:
///       藏了就是把用户自己的终端窗口弄没了。</item>
///   </list>
/// 另外:标准输出被重定向到文件 / 管道时(哪怕根本没有控制台)也该照常写 ——
/// 计划任务把输出重定向进一个文件是完全正常的用法,不能因为"没有窗口"就把这路输出也丢掉。
///
/// 判别规则收在纯函数 <see cref="Plan"/> 里,单独可测;本类其余部分只负责取事实与动手。
/// 这条规则错一半的代价很不对称(该藏没藏 = 开机闪黑窗;不该藏却藏了 = 把用户的终端弄没),
/// 所以它必须能被单元测试钉住,而不是只能靠真机试 —— 连"进程数取不到怎么办"这种
/// 只在异常路径上出现的分支也不例外(见 <see cref="Plan"/> 里的说明)。
/// </summary>
internal static class Terminal
{
    /// <summary>
    /// 启动时调用一次,<b>必须在任何输出之前</b> —— 双击启动时系统刚建好的那个控制台窗口
    /// 要尽快藏掉,晚了会看见它闪一下。
    ///
    /// 任何一步失败都只意味着"按没人看处理",不影响主流程。
    /// </summary>
    public static TerminalSetup Prepare()
    {
        IntPtr window;
        bool stdoutCaptured;
        uint attachedProcesses;

        try
        {
            window = GetConsoleWindow();
            stdoutCaptured = IsStdoutCaptured();
            attachedProcesses = window == IntPtr.Zero ? 0u : CountConsoleProcesses();
        }
        catch
        {
            // 判不出来:按"没人看"处理,而且绝不去动任何窗口
            return new TerminalSetup(
                new TerminalPlan(false, false, ConsoleOwnership.Unknown, false), false);
        }

        var plan = Plan(stdoutCaptured, window != IntPtr.Zero, attachedProcesses);

        bool windowHidden = false;
        if (plan.HideWindow)
        {
            try
            {
                ShowWindow(window, SwHide);

                // 复核一句:ShowWindow 的返回值表示的是"之前可不可见",判不了成败,
                // 所以藏完再问一次窗口到底还 Visible 不 Visible。日志里那句"已隐藏"要靠它。
                windowHidden = !IsWindowVisible(window);
            }
            catch
            {
                /* 藏不掉就让它去吧,不值得为此中断 */
            }
        }

        return new TerminalSetup(plan, windowHidden);
    }

    /// <summary>
    /// 纯判定:输出有没有被接走、控制台是不是我们自己独占的 → 该不该写终端、该不该藏窗口。
    ///
    /// 六种组合都要有明确答案,尤其是 <see cref="ConsoleOwnership.Inherited"/> 那一列:
    /// 继承来的控制台永远不许藏(那是用户的终端)。
    ///
    /// 进程数取不到时(<c>GetConsoleProcessList</c> 失败会返回 0)一律当作继承 ——
    /// 这是本方法唯一"判错就伤人"的分支:若把 0 也当成"只有我们自己",就会去藏窗口,
    /// 而在真的继承了父控制台的情况下,藏掉的是<b>用户自己正在用的那个终端窗口</b>。
    /// 宁可让本该藏的窗口留着(顶多闪一下),也不能赌。
    /// </summary>
    /// <param name="stdoutIsCaptured">标准输出是否指向文件 / 管道(重定向或被上游接走)。</param>
    /// <param name="hasConsoleWindow">本进程有没有关联的控制台窗口。</param>
    /// <param name="attachedProcessCount">
    /// 挂在同一个控制台上的进程数;1 = 只有我们自己 = 这个控制台是为我们新建的。
    /// 0 表示取不到,按"继承"处理。
    /// </param>
    public static TerminalPlan Plan(bool stdoutIsCaptured, bool hasConsoleWindow, uint attachedProcessCount)
    {
        ConsoleOwnership ownership =
            !hasConsoleWindow ? ConsoleOwnership.Missing
            : attachedProcessCount == 1 ? ConsoleOwnership.NewForUs
            : ConsoleOwnership.Inherited;

        // 有人在接输出就照常写,哪怕没有窗口(计划任务重定向进文件就是这种)
        bool writeToTerminal = stdoutIsCaptured || ownership == ConsoleOwnership.Inherited;

        // 只有"系统为我们新建的控制台"才藏;继承来的一律不动
        bool hideWindow = ownership == ConsoleOwnership.NewForUs;

        return new TerminalPlan(writeToTerminal, hideWindow, ownership, stdoutIsCaptured);
    }

    /// <summary>
    /// 标准输出是不是指向文件 / 管道。
    ///
    /// 直接问句柄类型,而不是看 <c>Console.IsOutputRedirected</c>:我们要的答案就是
    /// "这个句柄到底是什么",句柄类型是最直接的回答,也不用去猜那个属性在
    /// "既没有控制台、也没有重定向"的边界上怎么答。
    /// </summary>
    private static bool IsStdoutCaptured()
    {
        IntPtr handle = GetStdHandle(StdOutputHandle);
        if (handle == IntPtr.Zero || handle == InvalidHandleValue) return false;

        uint type = GetFileType(handle);
        return type == FileTypeDisk || type == FileTypePipe;
    }

    /// <summary>
    /// 挂在当前控制台上的进程数;1 = 只有我们自己 = 这个控制台是为我们新建的。
    /// <b>失败时返回 0</b>(Win32 的行为),调用方必须把它当成"不要动窗口"。
    /// </summary>
    private static uint CountConsoleProcesses()
    {
        // 只需要区分"是不是只有我",所以缓冲区够放下常见情况即可 ——
        // 挂满时返回值仍大于 1,结论一样
        var processIds = new uint[8];
        return GetConsoleProcessList(processIds, (uint)processIds.Length);
    }

    // 注:这里刻意<b>没有</b>"把输出编码设成 UTF-8"这一步,尽管本程序的文案全是中文。
    // Console.OutputEncoding 的 setter 在输出指向真控制台时会去调 SetConsoleOutputCP,而那是
    // <b>控制台窗口的属性,不是本进程的</b> —— 改完之后,同一个窗口里之后运行的所有程序都得
    // 按新代码页说话。一个"开热点"的小工具没资格替用户决定这件事。
    // 实测过的一半:输出被重定向到文件 / 管道时确实不会碰它(句柄不是控制台,936 保持 936);
    // 指向真控制台那一半没有实测(用 PowerShell 试过,但 PowerShell 自己调过 Console.SetOut,
    // .NET 的 setter 在这种情况下会跳过 SetConsoleOutputCP,那个探针说明不了问题)。
    // 之所以不去补测也不影响结论:不设它的根本理由是"跟随环境、不擅自改共享状态"——
    // 中文 Windows 的控制台本来就是 GBK(936),中文显示正常,什么都不做反而是对的;
    // 想要 UTF-8 的人自己在窗口里 chcp 65001 即可。
    // (log.txt 是另一回事:它固定 UTF-8 无 BOM,那是我们自己写的文件,见 AppendFileLogger。)

    // ===================================================================
    // Win32
    // ===================================================================

    private const int StdOutputHandle = -11;
    private const int SwHide = 0;
    private const uint FileTypeDisk = 1;
    private const uint FileTypePipe = 3;
    private static readonly IntPtr InvalidHandleValue = new IntPtr(-1);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("kernel32.dll")]
    private static extern uint GetConsoleProcessList(uint[] processList, uint processCount);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetStdHandle(int stdHandle);

    [DllImport("kernel32.dll")]
    private static extern uint GetFileType(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);
}
