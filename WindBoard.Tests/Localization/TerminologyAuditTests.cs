using System.Text.RegularExpressions;
using System.Xml.Linq;
using WindBoard.Tests;
using Xunit;

namespace WindBoard.Tests.Localization;

/// <summary>
/// 按 <c>docs/dev/guides/glossary.zh-CN.md</c> 检查资源值：禁用写法不出现，同一 key 的中英文用同一对名字。
/// </summary>
public sealed class TerminologyAuditTests
{
    /// <summary>中文禁用写法、应使用的写法，以及在检查中放行的 key。</summary>
    private static readonly (string Banned, string Preferred, string[] AllowedKeys)[] BannedChineseTerms =
    [
        ("笔划", "笔迹", []),
        ("笔画", "笔迹", []),
        ("画板", "画布", []),
        ("滑块", "滑条", []),
        ("调色板", "色板", []),
        ("边距", "留白", []),
        ("快捷栏", "Dock 或快捷入口", []),
        ("停靠栏", "Dock", []),
        ("数据文件夹", "数据目录", []),
        ("撤消", "撤销", []),
        ("素材", "元素", []),
        ("附件", "元素", []),
        ("图形", "形状", []),
        ("预设", "粗细档位", []),
        ("挡位", "粗细档位", []),
        ("归档", "压缩包", []),
        ("白板", "画布", ["MainWindow_Title"]),
    ];

    /// <summary>
    /// 中文与英文必须成对出现的术语。第三个成员是检查时排除的中文写法，
    /// 例如「页码范围」虽然含「页码」，但它是另一个词。
    /// </summary>
    private static readonly (string Chinese, string English, string[] ExcludedChinese)[] TermPairs =
    [
        ("画布", "canvas", []),
        ("页", "page", []),
        ("工作区", "workspace", []),
        ("笔迹", "stroke", []),
        ("形状", "shape", []),
        ("元素卡片", "element card", []),
        ("Dock", "Dock", []),
        ("快捷入口", "shortcut", []),
        ("色板", "palette", []),
        ("色块", "swatch", []),
        ("滑条", "slider", []),
        ("留白", "padding", []),
        ("文件夹", "folder", []),
        ("目录", "folder", []),
        ("页码", "page number", ["页码范围"]),
        ("撤销", "undo", []),
        ("重做", "redo", []),
        ("日志", "log", []),
        ("数据目录", "data folder", []),
    ];

    /// <summary>英文禁用写法：board 指界面时用 canvas，目录一律用 folder。</summary>
    private static readonly string[] BannedEnglishPatterns =
    [
        @"\bboard\b",
        @"\bdirector(y|ies)\b",
    ];

    [RepoFact]
    public void ChineseCopy_Should_Not_Use_BannedTerms()
    {
        (Dictionary<string, string> chinese, _) = LoadResw();

        List<string> problems = [];
        foreach ((string banned, string preferred, string[] allowedKeys) in BannedChineseTerms)
        {
            foreach ((string key, string value) in chinese)
            {
                if (value.Contains(banned, StringComparison.Ordinal)
                    && !Array.Exists(allowedKeys, allowed => string.Equals(allowed, key, StringComparison.Ordinal)))
                {
                    problems.Add($"{key}：含「{banned}」，应写「{preferred}」");
                }
            }
        }

        Assert.True(
            problems.Count == 0,
            "资源值出现术语表禁止的写法：\n" + string.Join('\n', problems));
    }

    [RepoFact]
    public void ChineseAndEnglishCopy_Should_Use_The_SameTerm()
    {
        (Dictionary<string, string> chinese, Dictionary<string, string> english) = LoadResw();

        List<string> problems = [];
        foreach ((string chineseTerm, string englishTerm, string[] excludedChinese) in TermPairs)
        {
            foreach ((string key, string chineseValue) in chinese)
            {
                if (!chineseValue.Contains(chineseTerm, StringComparison.Ordinal)
                    || Array.Exists(excludedChinese, excluded => chineseValue.Contains(excluded, StringComparison.Ordinal)))
                {
                    continue;
                }

                if (!english.TryGetValue(key, out string? englishValue)
                    || englishValue.Contains(englishTerm, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                problems.Add($"{key}：中文用「{chineseTerm}」，英文未用「{englishTerm}」（{englishValue}）");
            }
        }

        Assert.True(
            problems.Count == 0,
            "同一 key 的中英文用了不同的术语：\n" + string.Join('\n', problems));
    }

    [RepoFact]
    public void EnglishCopy_Should_Not_Use_BannedPatterns()
    {
        (_, Dictionary<string, string> english) = LoadResw();

        List<string> problems = [];
        foreach ((string key, string value) in english)
        {
            if (BannedEnglishPatterns.Any(pattern => Regex.IsMatch(value, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)))
            {
                problems.Add($"{key}：{value}");
            }
        }

        Assert.True(
            problems.Count == 0,
            "英文资源值出现术语表禁止的写法：\n" + string.Join('\n', problems));
    }

    private static (Dictionary<string, string> Chinese, Dictionary<string, string> English) LoadResw()
    {
        DirectoryInfo? repoRoot = RepoRootLocator.Find();
        Assert.NotNull(repoRoot);

        string stringsRoot = Path.Combine(repoRoot!.FullName, "WindBoard", "Strings");
        return (LoadLocale(stringsRoot, "zh-CN"), LoadLocale(stringsRoot, "en-US"));
    }

    private static Dictionary<string, string> LoadLocale(string stringsRoot, string locale)
    {
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        foreach (string filePath in Directory.EnumerateFiles(Path.Combine(stringsRoot, locale), "*.resw"))
        {
            XDocument document = XDocument.Load(filePath);
            foreach (XElement data in document.Descendants("data"))
            {
                string? key = data.Attribute("name")?.Value;
                string? value = data.Element("value")?.Value;
                if (key is not null && value is not null)
                {
                    values[key] = value;
                }
            }
        }

        return values;
    }
}
