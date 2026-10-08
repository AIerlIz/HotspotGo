using System.Collections.Generic;
using System.Reflection;
using HotspotGo.Cli;
using Xunit;

namespace HotspotGo.Tests.Cli;

/// <summary>
/// 版本信息(<c>--version</c> / <c>-v</c>)。
///
/// 产品名与版本号都来自程序集自身(发布时按 tag 注入版本、产品名在 csproj 的 &lt;Product&gt;),
/// 代码里不另存一份。所以这里测两头:纯排版函数的边界(没有提交号 / 空产品名 / null),
/// 以及"真程序集里读得到、而且读的就是属性里那份" —— 后者能挡住
/// "属性没注入 / 读法写错 / 又抄了一份常量"这类只在发版时才暴露的问题。
/// </summary>
public class VersionTextTests
{
    private const string Product = "热点快启 HotspotGo";

    /// <summary>带提交号时:第一行产品名 + 版本号,第二行完整提交号(报障时能直接定位到提交)。</summary>
    [Fact]
    public void Format_splits_version_and_commit()
    {
        var lines = VersionText.Format(Product, "0.2.0+76cee162ede52d56947e3ddfc8fec4a5fa9b7ac1");

        Assert.Equal(2, lines.Count);
        Assert.StartsWith(Product, lines[0]);
        Assert.Contains("0.2.0", lines[0]);
        Assert.Contains("76cee162ede52d56947e3ddfc8fec4a5fa9b7ac1", lines[1]);
    }

    /// <summary>没有提交号(本地构建、或版本里不含提交信息)时只有一行,不能多出一个空行。</summary>
    [Fact]
    public void Format_without_commit_produces_one_line()
    {
        var lines = VersionText.Format(Product, "1.2.3");

        Assert.Single(lines);
        Assert.Equal(Product + " 1.2.3", lines[0]);
    }

    /// <summary>取不到版本号时显示"未知",而不是崩掉或显示一片空白。</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Format_survives_missing_version(string missing)
    {
        var lines = VersionText.Format(Product, missing);

        Assert.Single(lines);
        Assert.Contains("未知", lines[0]);
    }

    /// <summary>产品名拿不到时只显示版本号,而且不能留下一个前导空格(那是排版毛刺)。</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Format_survives_missing_product_name(string missing)
    {
        var lines = VersionText.Format(missing, "1.2.3");

        Assert.Single(lines);
        Assert.Equal("1.2.3", lines[0]);
    }

    /// <summary>
    /// 每行都必须是完整的一行 —— 带上换行会破坏"日志每行都带时间戳"这个约定
    /// (见 UsageText 里的说明),冒烟脚本也按它判断日志是否完整。
    /// </summary>
    [Fact]
    public void Format_never_returns_multiline_or_empty_entries()
    {
        foreach (var line in VersionText.Format(Product, "0.2.0+abc123"))
        {
            Assert.False(string.IsNullOrWhiteSpace(line));
            Assert.DoesNotContain("\n", line);
            Assert.DoesNotContain("\r", line);
        }
    }

    /// <summary>
    /// 真程序集里必须读得到版本信息:这条链是"tag → 程序集属性 → 终端 / log.txt"的最后一环,
    /// 断了的话 <c>-v</c> 只会显示"未知",而这只有在发版后才看得出来。
    /// </summary>
    [Fact]
    public void Build_reads_the_real_assembly()
    {
        IReadOnlyList<string> lines = VersionText.Build();

        Assert.NotEmpty(lines);
        Assert.DoesNotContain("未知", lines[0]);
    }

    /// <summary>
    /// 产品名必须来自程序集属性(即 csproj 的 <c>&lt;Product&gt;</c>),不是代码里另写的常量。
    /// 这里按属性读出来对照,免得测试自己又抄一份产品名 —— 那正是这次要消掉的东西。
    /// </summary>
    [Fact]
    public void Build_uses_the_product_name_from_the_assembly()
    {
        var expected = typeof(VersionText).Assembly
            .GetCustomAttribute<AssemblyProductAttribute>()?.Product;

        Assert.False(string.IsNullOrWhiteSpace(expected));
        Assert.StartsWith(expected, VersionText.Build()[0]);
    }
}
