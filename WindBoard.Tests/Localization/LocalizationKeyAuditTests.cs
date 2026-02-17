using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Resources;
using System.Text.RegularExpressions;
using WindBoard.Localization;
using Xunit;

namespace WindBoard.Tests.Localization;

public sealed class LocalizationKeyAuditTests
{
    [Fact]
    public void All_LocKeys_Should_Exist_In_DefaultLanguageResx()
    {
        DirectoryInfo? repoRoot = FindRepoRoot();
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

        HashSet<string> resxKeys = CollectDefaultLanguageResxKeys();

        string[] missingKeys = usedKeys
            .Except(resxKeys)
            .OrderBy(static k => k, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            missingKeys.Length == 0,
            "以下 Key 在 XAML/C# 中被引用，但未在默认语言资源中定义：\n"
            + string.Join('\n', missingKeys));
    }

    private static DirectoryInfo? FindRepoRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        for (int i = 0; i < 20 && current is not null; i++)
        {
            if (File.Exists(Path.Combine(current.FullName, "WindBoard.slnx")))
            {
                return current;
            }

            current = current.Parent;
        }

        return null;
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

    private static HashSet<string> CollectDefaultLanguageResxKeys()
    {
        const string defaultCultureName = "zh-CN";

        Assembly assembly = typeof(LocExtension).Assembly;
        ResourceIndex index = BuildResourceIndex(assembly);

        string defaultLanguageSegment = GetDefaultLanguageSegment(index, defaultCultureName);
        IReadOnlyCollection<string> features = index.GetFeatures(defaultLanguageSegment);

        Assert.True(features.Count > 0, $"默认语言资源为空：languageSegment={defaultLanguageSegment}");

        HashSet<string> keys = new(StringComparer.Ordinal);

        foreach (string feature in features)
        {
            ResourceManager manager = new($"WindBoard.Localization.{defaultLanguageSegment}.{feature}", assembly);
            ResourceSet? resourceSet = manager.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: false);

            Assert.True(resourceSet is not null, $"无法读取默认语言资源集：languageSegment={defaultLanguageSegment}, feature={feature}");

            foreach (DictionaryEntry entry in resourceSet!)
            {
                if (entry.Key is string key && !string.IsNullOrWhiteSpace(key))
                {
                    keys.Add(key);
                }
            }
        }

        return keys;
    }

    private static string GetDefaultLanguageSegment(ResourceIndex index, string defaultCultureName)
    {
        foreach (string candidate in EnumerateLanguageSegmentCandidates(defaultCultureName))
        {
            if (index.HasLanguageSegment(candidate))
            {
                return candidate;
            }
        }

        Assert.Fail($"未找到默认语言资源：{defaultCultureName}（候选：{string.Join(", ", EnumerateLanguageSegmentCandidates(defaultCultureName))}）");
        return defaultCultureName;
    }

    private static IEnumerable<string> EnumerateLanguageSegmentCandidates(string cultureName)
    {
        yield return cultureName;

        if (cultureName.Contains('-', StringComparison.Ordinal))
        {
            string normalized = cultureName.Replace('-', '_');
            if (!normalized.Equals(cultureName, StringComparison.Ordinal))
            {
                yield return normalized;
            }
        }
    }

    private static ResourceIndex BuildResourceIndex(Assembly assembly)
    {
        ResourceIndex index = new();
        string[] names = assembly.GetManifestResourceNames();

        const string prefix = "WindBoard.Localization.";
        const string suffix = ".resources";

        foreach (string name in names)
        {
            if (!name.StartsWith(prefix, StringComparison.Ordinal) || !name.EndsWith(suffix, StringComparison.Ordinal))
            {
                continue;
            }

            string payload = name.Substring(prefix.Length, name.Length - prefix.Length - suffix.Length);
            int dot = payload.IndexOf('.');
            if (dot <= 0 || dot >= payload.Length - 1)
            {
                continue;
            }

            string languageSegment = payload.Substring(0, dot);
            string feature = payload.Substring(dot + 1);

            index.Add(languageSegment, feature);
        }

        return index;
    }

    private sealed class ResourceIndex
    {
        private readonly Dictionary<string, HashSet<string>> _featuresByLanguageSegment = new(StringComparer.Ordinal);

        internal void Add(string languageSegment, string feature)
        {
            if (!_featuresByLanguageSegment.TryGetValue(languageSegment, out HashSet<string>? features))
            {
                features = new HashSet<string>(StringComparer.Ordinal);
                _featuresByLanguageSegment.Add(languageSegment, features);
            }

            features.Add(feature);
        }

        internal bool HasLanguageSegment(string languageSegment)
        {
            return _featuresByLanguageSegment.ContainsKey(languageSegment);
        }

        internal IReadOnlyCollection<string> GetFeatures(string languageSegment)
        {
            if (_featuresByLanguageSegment.TryGetValue(languageSegment, out HashSet<string>? features))
            {
                return features;
            }

            return Array.Empty<string>();
        }
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
