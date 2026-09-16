using Xunit;

namespace HotspotGo.Tests;

/// <summary>
/// 退出码契约。
///
/// 这些数字是给脚本 / 计划任务看的对外接口(见 ExitCode 的注释),一旦变动会
/// 静默破坏调用方的判断逻辑,而且不会有任何编译错误提醒 —— 所以在这里锁死。
/// </summary>
public class ExitCodeTests
{
    [Fact]
    public void Exit_codes_keep_their_documented_values()
    {
        Assert.Equal(0, (int)ExitCode.Ok);
        Assert.Equal(1, (int)ExitCode.Fatal);
        Assert.Equal(2, (int)ExitCode.NoUpstream);
        Assert.Equal(3, (int)ExitCode.NoManager);
        Assert.Equal(4, (int)ExitCode.StartFailed);
    }
}
