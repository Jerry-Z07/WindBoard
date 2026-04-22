using System.Text.RegularExpressions;
using WindBoard.Tests;
using Xunit;

namespace WindBoard.Tests.Localization;

public sealed class LocalizationKeyAuditTests
{
    [Fact]
    public void All_LocKeys_Should_Exist_In_DefaultLanguageResw()
    {
        DirectoryInfo? repoRoot = RepoRootLocator.Find();
        if (repoRoot is null)
        {
            SkipOrReturn("未找到 WindBoard.slnx，跳过本地化 Key 审计测试。");
            return;
        }

        string windBoardDir = Path.Combine(repoRoot.FullName, "WindBoard");
        if (!Directory.Exists(windBoardDir))
        {
            SkipOrReturn("未找到 WindBoard 目录，跳过本地化 Key 审计测试。");
            return;
        }

        HashSet<string> xamlKeys = CollectXamlLocKeys(windBoardDir);
        HashSet<string> csKeys = CollectCSharpLocKeys(windBoardDir, out List<string> dynamicKeyUsages);
        HashSet<string> usedKeys = new(StringComparer.Ordinal);
        usedKeys.UnionWith(xamlKeys);
        usedKeys.UnionWith(csKeys);

        Assert.True(
            dynamicKeyUsages.Count == 0,
            "发现不允许的动态 Key 调用（要求 L10n.Get/Format 的 key 参数使用字符串字面量）：\n"
            + string.Join('\n', dynamicKeyUsages));

        HashSet<string> reswKeys = CollectDefaultLanguageReswKeys(windBoardDir);

        string[] missingKeys = usedKeys
            .Except(reswKeys)
            .OrderBy(static k => k, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            missingKeys.Length == 0,
            "以下 Key 在 XAML/C# 中被引用，但未在默认语言资源中定义：\n"
            + string.Join('\n', missingKeys));
    }

    private static HashSet<string> CollectXamlLocKeys(string windBoardDir)
    {
        Regex locRegex = new(
            @"\{l10n:Loc\b[^}]*\bKey\s*=\s*(?:'(?<key>[^']+)'|""(?<key>[^""]+)""|(?<key>[^\s,}]+))",
            RegexOptions.Compiled);

        HashSet<string> keys = new(StringComparer.Ordinal);
        foreach (string filePath in EnumerateProjectFiles(windBoardDir, "*.xaml"))
        {
            string text = File.ReadAllText(filePath);
            foreach (Match match in locRegex.Matches(text))
            {
                string key = match.Groups["key"].Value.Trim();
                if (!string.IsNullOrEmpty(key))
                {
                    keys.Add(key);
                }
            }
        }

        return keys;
    }

    private static HashSet<string> CollectCSharpLocKeys(string windBoardDir, out List<string> dynamicKeyUsages)
    {
        Regex callRegex = new(@"\bL10n\.(?:Get|Format)\s*\(", RegexOptions.Compiled);

        HashSet<string> keys = new(StringComparer.Ordinal);
        dynamicKeyUsages = new List<string>();

        foreach (string filePath in EnumerateProjectFiles(windBoardDir, "*.cs"))
        {
            // 本地化基础设施本身会用变量 key（例如 XAML MarkupExtension），这里不纳入“必须字面量”的审计范围。
            if (filePath.Contains($"{Path.DirectorySeparatorChar}Localization{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string text = File.ReadAllText(filePath);
            foreach (Match match in callRegex.Matches(text))
            {
                int openParenIndex = match.Index + match.Length - 1;
                if (TryParseKeyStringLiteral(text, openParenIndex + 1, out string key))
                {
                    if (!string.IsNullOrWhiteSpace(key))
                    {
                        keys.Add(key);
                    }
                }
                else
                {
                    (int line, int column) = GetLineColumn(text, match.Index);
                    dynamicKeyUsages.Add($"{NormalizePath(filePath)}:{line}:{column} - {match.Value}...");
                }
            }
        }

        return keys;
    }

    private static HashSet<string> CollectDefaultLanguageReswKeys(string windBoardDir)
    {
        const string defaultCultureName = "zh-CN";
        string stringsDir = Path.Combine(windBoardDir, "Strings", defaultCultureName);
        Assert.True(Directory.Exists(stringsDir), $"未找到默认语言资源目录：{stringsDir}");

        HashSet<string> keys = new(StringComparer.Ordinal);
        Regex keyRegex = new(@"<data\s+name=""(?<key>[^""]+)""", RegexOptions.Compiled);

        foreach (string filePath in Directory.EnumerateFiles(stringsDir, "*.resw", SearchOption.TopDirectoryOnly))
        {
            string text = File.ReadAllText(filePath);
            foreach (Match match in keyRegex.Matches(text))
            {
                string key = match.Groups["key"].Value.Trim();
                if (!string.IsNullOrWhiteSpace(key))
                {
                    keys.Add(key);
                }
            }
        }

        Assert.True(keys.Count > 0, $"默认语言资源为空：{stringsDir}");

        return keys;
    }

    private static IEnumerable<string> EnumerateProjectFiles(string windBoardDir, string pattern)
    {
        foreach (string filePath in Directory.EnumerateFiles(windBoardDir, pattern, SearchOption.AllDirectories))
        {
            if (filePath.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                || filePath.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return filePath;
        }
    }

    private static bool TryParseKeyStringLiteral(string text, int startIndex, out string key)
    {
        key = string.Empty;

        int i = SkipWhitespace(text, startIndex);

        // 允许 named argument：key: "..."
        i = SkipOptionalNamedArgument(text, i, "key");

        if (i >= text.Length)
        {
            return false;
        }

        // verbatim string: @"..."
        if (text[i] == '@')
        {
            if (i + 1 >= text.Length || text[i + 1] != '"')
            {
                return false;
            }

            i += 2;
            int start = i;
            while (i < text.Length)
            {
                if (text[i] == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        // "" -> "
                        i += 2;
                        continue;
                    }

                    key = text.Substring(start, i - start).Replace("\"\"", "\"", StringComparison.Ordinal);
                    return true;
                }

                i++;
            }

            return false;
        }

        // normal string: "..."
        if (text[i] != '"')
        {
            return false;
        }

        i++;
        int literalStart = i;
        while (i < text.Length)
        {
            char c = text[i];
            if (c == '\\')
            {
                // 跳过转义字符（key 约定不包含转义，但这里做基本兼容）
                i += 2;
                continue;
            }

            if (c == '"')
            {
                key = text.Substring(literalStart, i - literalStart);
                return true;
            }

            i++;
        }

        return false;
    }

    private static int SkipWhitespace(string text, int index)
    {
        while (index < text.Length && char.IsWhiteSpace(text[index]))
        {
            index++;
        }

        return index;
    }

    private static int SkipOptionalNamedArgument(string text, int index, string argumentName)
    {
        int i = index;
        if (!IsIdentifierStart(text, i))
        {
            return index;
        }

        int start = i;
        i++;
        while (i < text.Length && IsIdentifierPart(text[i]))
        {
            i++;
        }

        ReadOnlySpan<char> ident = text.AsSpan(start, i - start);
        if (!ident.Equals(argumentName, StringComparison.Ordinal))
        {
            return index;
        }

        i = SkipWhitespace(text, i);
        if (i < text.Length && text[i] == ':')
        {
            i++;
            return SkipWhitespace(text, i);
        }

        return index;
    }

    private static bool IsIdentifierStart(string text, int index)
    {
        return index < text.Length && (char.IsLetter(text[index]) || text[index] == '_');
    }

    private static bool IsIdentifierPart(char c)
    {
        return char.IsLetterOrDigit(c) || c == '_';
    }

    private static (int line, int column) GetLineColumn(string text, int index)
    {
        int line = 1;
        int lastNewLine = -1;

        int max = Math.Min(index, text.Length);
        for (int i = 0; i < max; i++)
        {
            if (text[i] == '\n')
            {
                line++;
                lastNewLine = i;
            }
        }

        return (line, index - lastNewLine);
    }

    private static string NormalizePath(string path)
    {
        return path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
    }

    private static void SkipOrReturn(string reason)
    {
        // xUnit 2 允许通过抛出 SkipException 在运行时跳过；如果运行环境不支持，则直接返回（视为通过）。
        Exception? skip = CreateSkipException(reason);
        if (skip is not null)
        {
            throw skip;
        }
    }

    private static Exception? CreateSkipException(string reason)
    {
        string[] candidates =
        [
            "Xunit.Sdk.SkipException, xunit.core",
            "Xunit.Sdk.SkipException, xunit.assert",
            "Xunit.Sdk.SkipException, xunit.execution.dotnet",
            "Xunit.Sdk.SkipException, xunit.execution.desktop",
        ];

        foreach (string typeName in candidates)
        {
            Type? type = Type.GetType(typeName, throwOnError: false);
            if (type is not null)
            {
                return Activator.CreateInstance(type, reason) as Exception;
            }
        }

        return null;
    }
}
