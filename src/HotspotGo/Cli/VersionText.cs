using System;
using System.Collections.Generic;
using System.Reflection;

namespace HotspotGo.Cli;

/// <summary>
/// 版本信息(对应 <c>--version</c> / <c>-v</c>)。
///
/// 版本号与产品名都从程序集自己身上读,而不是在代码里再写一份常量:发布流水线是按 tag
/// 注入版本的(release.yml 传 <c>-p:Version=</c>)、产品名写在 csproj 的 <c>&lt;Product&gt;</c> 里
/// —— 代码里再抄一份,迟早改一处忘一处,于是 <c>-v</c> 和 exe 文件属性对不上。
///
/// 版本号读的是 <see cref="AssemblyInformationalVersionAttribute"/> —— 注入之后它长这样:
/// <c>0.2.0+76cee162ede52d56947e3ddfc8fec4a5fa9b7ac1</c>,即"版本号 + 构建它的那个提交"。
/// 报障时后面那串比版本号本身有用得多(能直接定位到是哪次提交构建的)。
/// </summary>
internal static class VersionText
{
    /// <summary>
    /// 读本程序集真实的产品名与版本信息并排版。读不到也不会抛,最差显示"未知"。
    /// </summary>
    public static IReadOnlyList<string> Build()
    {
        var assembly = typeof(VersionText).Assembly;

        return Format(ProductNameOf(assembly), InformationalVersionOf(assembly));
    }

    /// <summary>
    /// 把产品名与版本排成给人看的几行。<b>纯函数</b>,单独可测 ——
    /// 拼接规则里的边界(没有 + 号、空产品名、null)都在测试里覆盖。
    ///
    /// 每行都必须是完整的一行,不能自带换行:日志每行要带时间戳(见 <see cref="UsageText"/> 里的说明)。
    /// </summary>
    /// <param name="productName">产品名;空则只显示版本号,不留前导空格。</param>
    /// <param name="informationalVersion">形如 <c>1.2.3+abcdef</c>;可以是 null 或空。</param>
    public static IReadOnlyList<string> Format(string productName, string informationalVersion)
    {
        string text = (informationalVersion ?? string.Empty).Trim();

        // "1.2.3+sha" → 版本号与提交号分开;没有 + 号(本地构建、或版本里不含提交信息)时只有版本号
        int plus = text.IndexOf('+');
        string version = plus < 0 ? text : text.Substring(0, plus).Trim();
        string commit = plus < 0 ? string.Empty : text.Substring(plus + 1).Trim();

        string name = (productName ?? string.Empty).Trim();
        string prefix = name.Length == 0 ? string.Empty : name + " ";

        var lines = new List<string> { prefix + (version.Length == 0 ? "未知" : version) };

        if (commit.Length > 0) lines.Add("构建 " + commit);

        return lines;
    }

    /// <summary>
    /// 产品名取自 <see cref="AssemblyProductAttribute"/>(就是 csproj 里的 <c>&lt;Product&gt;</c>)。
    /// 属性没生成时退到程序集名 —— 总比显示空白强。
    /// </summary>
    private static string ProductNameOf(Assembly assembly)
    {
        var attribute = (AssemblyProductAttribute)Attribute.GetCustomAttribute(
            assembly, typeof(AssemblyProductAttribute));

        return string.IsNullOrWhiteSpace(attribute?.Product)
            ? assembly.GetName().Name
            : attribute.Product;
    }

    /// <summary>取程序集的版本信息字符串;取不到返回 null(排版那边会兜成"未知")。</summary>
    private static string InformationalVersionOf(Assembly assembly)
    {
        var attribute = (AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(
            assembly, typeof(AssemblyInformationalVersionAttribute));

        return attribute?.InformationalVersion;
    }
}
