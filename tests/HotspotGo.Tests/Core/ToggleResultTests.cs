using System;
using HotspotGo.Core;
using Xunit;

namespace HotspotGo.Tests.Core;

/// <summary>
/// 开关结果对象。
///
/// 它一头连着业务判断(成不成功 → 退出码),一头连着 log.txt 里那一行,
/// 所以字段和 <c>Describe()</c> 文本两个方向都锁死。
/// </summary>
public class ToggleResultTests
{
    /// <summary>成功结果:状态达标、记下前后状态与耗时,没有错误信息。</summary>
    [Fact]
    public void Success_carries_state_transition()
    {
        var result = ToggleResult.Success("Off", "On", TimeSpan.FromMilliseconds(1234));

        Assert.True(result.Succeeded);
        Assert.Equal("Off", result.StateBefore);
        Assert.Equal("On", result.StateAfter);
        Assert.Equal(TimeSpan.FromMilliseconds(1234), result.Elapsed);
        Assert.Null(result.Error);
    }

    /// <summary>成功时的可读文本要带上状态迁移和耗时(写进 log.txt 的那一行)。</summary>
    [Fact]
    public void Success_describes_transition()
    {
        var describe = ToggleResult.Success("Off", "On", TimeSpan.FromMilliseconds(1234)).Describe();

        Assert.Contains("成功", describe);
        Assert.Contains("Off → On", describe);
        Assert.Contains("1234", describe);
    }

    /// <summary>失败结果:没有耗时,带着原因。</summary>
    [Fact]
    public void Failure_carries_reason()
    {
        var result = ToggleResult.Failure("Off", "等待超时");

        Assert.False(result.Succeeded);
        Assert.Equal("Off", result.StateAfter);
        Assert.Equal("等待超时", result.Error);
        Assert.Null(result.Elapsed);
    }

    /// <summary>失败时的可读文本要带上原因。</summary>
    [Fact]
    public void Failure_describes_reason()
    {
        var describe = ToggleResult.Failure("Off", "等待超时").Describe();

        Assert.Contains("失败", describe);
        Assert.Contains("等待超时", describe);
    }
}
