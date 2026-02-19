using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Resources;
using WindBoard.Logging;

namespace WindBoard.Localization
{
    /// <summary>
    /// 本地化字符串读取入口（基于 .resx）。
    ///
    /// 设计说明：
    /// - 选择 .resx：这是 .NET 生态里最通用的资源形式，天然支持卫星程序集（多语言）与工具链；
    /// - 缺失 Key 不应导致崩溃：这里会回退为 fallback（或 key 本身），并仅记录一次日志，便于排查漏配。
    /// </summary>
    internal static class L10n
    {
        /// <summary>
        /// 默认语言（项目当前以中文填充）。
        ///
        /// 注意：这里用 Culture 名称做“对外语义”，实际嵌入程序集的资源名可能会因为 MSBuild 的命名规则
        /// 将 '-' 规范化为 '_'（例如 zh-CN -> zh_CN）。运行时会自动兼容两种命名。
        /// </summary>
        private const string DefaultCultureName = "zh-CN";

        // 每个缺失 Key 只打一次日志，避免 UI 高频取值刷屏。
        private static readonly ConcurrentDictionary<string, byte> MissingKeyLogged = new(StringComparer.Ordinal);

        // 缺翻译日志：按 culture+key 去重（仅在“该 culture 已部分提供资源”时才会记录）。
        private static readonly ConcurrentDictionary<string, byte> MissingTranslationLogged = new(StringComparer.Ordinal);

        // 不支持的语言：按 culture 去重，避免整套 UI 取值时刷屏。
        private static readonly ConcurrentDictionary<string, byte> UnsupportedCultureLogged = new(StringComparer.Ordinal);

        // 资源加载/读取异常：按 baseName 去重，避免异常高频时刷屏。
        private static readonly ConcurrentDictionary<string, byte> ResourceErrorLogged = new(StringComparer.Ordinal);

        // 资源索引：用于避免“资源不存在 -> 走异常路径”的性能问题。
        private static readonly Lazy<ResourceIndex> Index = new(BuildResourceIndex, isThreadSafe: true);

        // ResourceManager 缓存（按 语言段 + 功能）。
        private static readonly ConcurrentDictionary<(string LanguageSegment, string Feature), ResourceManager> ResourceManagers = new();

        /// <summary>
        /// 初始化入口（可选）。
        /// 目前用于尽早触发一次资源加载，便于在启动阶段就发现资源打包/命名错误。
        /// </summary>
        internal static void Initialize()
        {
            try
            {
                ResourceIndex index = Index.Value;

                string? defaultSegment = GetDefaultLanguageSegment(index);
                if (string.IsNullOrWhiteSpace(defaultSegment))
                {
                    AppLog.Error("L10n", $"未找到默认语言资源：{DefaultCultureName}");
                    return;
                }

                IReadOnlyCollection<string> defaultFeatures = index.GetFeatures(defaultSegment);
                if (defaultFeatures.Count == 0)
                {
                    AppLog.Error("L10n", $"默认语言资源为空：languageSegment={defaultSegment}");
                    return;
                }

                // 1) 优先验证默认语言：缺它会导致 UI 全面降级。
                foreach (string feature in defaultFeatures)
                {
                    TryPreloadResourceSet(defaultSegment, feature);
                }

                // 2) 若当前 UI 语言有对应语言资源，则预加载已有的功能资源，便于尽早发现打包/命名错误。
                //    说明：未提供该语言资源属于正常情况，不应打 Error。
                CultureInfo uiCulture = CultureInfo.CurrentUICulture;
                foreach (string segment in EnumerateCultureFallbackSegments(uiCulture, index, defaultSegment))
                {
                    if (segment == defaultSegment)
                    {
                        continue;
                    }

                    foreach (string feature in defaultFeatures)
                    {
                        if (index.HasBase(segment, feature))
                        {
                            TryPreloadResourceSet(segment, feature);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("L10n", "初始化失败", ex);
            }
        }

        /// <summary>
        /// 获取当前程序包内“已提供资源”的语言列表（不包含 "system"）。
        /// 
        /// 说明：
        /// - 语言来源于程序集嵌入的资源名（WindBoard.Localization.&lt;Language&gt;.&lt;Feature&gt;.resources），因此无需维护枚举；
        /// - 返回值为 <see cref="CultureInfo.Name"/> 的规范形式（例如 "zh-CN" / "en-US" / "ja-JP"）；
        /// - 顺序稳定：默认语言优先，其余按字符串排序。
        /// </summary>
        internal static IReadOnlyList<string> GetSupportedCultureNames()
        {
            try
            {
                ResourceIndex index = Index.Value;

                // 使用 IgnoreCase 去重：避免同时出现 zh-CN 与 zh_CN 等重复项。
                HashSet<string> cultures = new(StringComparer.OrdinalIgnoreCase);

                foreach (string segment in index.LanguageSegments)
                {
                    if (string.IsNullOrWhiteSpace(segment))
                    {
                        continue;
                    }

                    // 兼容：资源段中可能包含 '_'，统一映射为 CultureInfo 可识别的 '-'。
                    string candidate = segment.Replace('_', '-');
                    try
                    {
                        cultures.Add(CultureInfo.GetCultureInfo(candidate).Name);
                    }
                    catch
                    {
                        // 忽略无效 Culture：不把它暴露到 UI 选择中，避免用户选择后无法应用。
                    }
                }

                // 兜底：确保默认语言一定存在（即使资源索引异常或某些构建规则导致未被扫描）。
                cultures.Add(CultureInfo.GetCultureInfo(DefaultCultureName).Name);

                List<string> result = new(cultures.Count);
                foreach (string name in cultures)
                {
                    result.Add(name);
                }

                result.Sort(StringComparer.OrdinalIgnoreCase);

                // 默认语言置顶
                string defaultName = CultureInfo.GetCultureInfo(DefaultCultureName).Name;
                for (int i = 0; i < result.Count; i++)
                {
                    if (result[i].Equals(defaultName, StringComparison.OrdinalIgnoreCase))
                    {
                        if (i != 0)
                        {
                            result.RemoveAt(i);
                            result.Insert(0, defaultName);
                        }

                        break;
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                AppLog.Warn("L10n", "获取可用语言列表失败，将回退到仅默认语言", ex);
                return new[] { DefaultCultureName };
            }
        }

        /// <summary>
        /// 获取本地化字符串。
        /// </summary>
        /// <param name="key">资源 Key（建议使用常量字符串）。</param>
        /// <param name="fallback">找不到资源时的回退文案；为 null 时回退为 key。</param>
        internal static string Get(string key, string? fallback = null)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return fallback ?? string.Empty;
            }

            try
            {
                ResourceIndex index = Index.Value;

                string? defaultSegment = GetDefaultLanguageSegment(index);
                if (string.IsNullOrWhiteSpace(defaultSegment))
                {
                    LogResourceErrorOnce($"WindBoard.Localization.{DefaultCultureName}.*", "默认语言资源缺失");
                    return fallback ?? key;
                }

                string feature = ParseFeatureFromKey(key);
                CultureInfo uiCulture = CultureInfo.CurrentUICulture;
                string requestedCultureName = uiCulture.Name;

                // 判断：当前语言（含 parent）是否“至少存在一个资源文件”。
                // - 不存在：说明项目并未提供该语言；此时不应按 key 刷屏记录“缺翻译”。
                // - 存在：说明在做部分翻译；此时缺失的 key 记录一次 Warn，便于补全。
                bool hasAnyRequestedLanguageResources = HasAnyLanguageResources(index, uiCulture, defaultSegment);

                foreach (string segment in EnumerateCultureFallbackSegments(uiCulture, index, defaultSegment))
                {
                    // 语言存在但该功能文件不存在（例如只翻译了部分模块）：继续回退。
                    if (!index.HasBase(segment, feature))
                    {
                        continue;
                    }

                    if (!TryGetString(segment, feature, key, out string? value))
                    {
                        continue;
                    }

                    if (!string.IsNullOrEmpty(value))
                    {
                        // 若使用了默认语言，且当前 culture 并非默认语言：按策略输出缺翻译日志。
                        if (segment == defaultSegment
                            && !string.IsNullOrWhiteSpace(requestedCultureName)
                            && !requestedCultureName.Equals(DefaultCultureName, StringComparison.Ordinal))
                        {
                            if (!hasAnyRequestedLanguageResources)
                            {
                                LogUnsupportedCultureOnce(requestedCultureName);
                            }
                            else
                            {
                                LogMissingTranslationOnce(requestedCultureName, key);
                            }
                        }

                        return value;
                    }
                }

                LogMissingKeyOnce(key);
                return fallback ?? key;
            }
            catch (Exception ex)
            {
                AppLog.Error("L10n", $"读取失败：key={key}", ex);
                return fallback ?? key;
            }
        }

        /// <summary>
        /// 获取本地化字符串，并按当前语言进行格式化。
        /// </summary>
        internal static string Format(string key, params object?[] args)
        {
            string template = Get(key, fallback: key);
            try
            {
                return string.Format(CultureInfo.CurrentCulture, template, args);
            }
            catch (Exception ex)
            {
                AppLog.Error("L10n", $"格式化失败：key={key}, template='{template}'", ex);
                return template;
            }
        }

        private static void LogMissingKeyOnce(string key)
        {
            if (MissingKeyLogged.TryAdd(key, 0))
            {
                AppLog.Warn("L10n", $"缺少资源 key：{key}");
            }
        }

        private static void LogMissingTranslationOnce(string cultureName, string key)
        {
            string logKey = $"{cultureName}:{key}";
            if (MissingTranslationLogged.TryAdd(logKey, 0))
            {
                AppLog.Warn("L10n", $"缺少翻译：culture={cultureName}, key={key}，已回退到 {DefaultCultureName}");
            }
        }

        private static void LogUnsupportedCultureOnce(string cultureName)
        {
            if (UnsupportedCultureLogged.TryAdd(cultureName, 0))
            {
                AppLog.Info("L10n", $"未提供语言资源：culture={cultureName}，已回退到 {DefaultCultureName}");
            }
        }

        private static void LogResourceErrorOnce(string baseName, string action, Exception? ex = null)
        {
            string logKey = $"{action}:{baseName}";
            if (ResourceErrorLogged.TryAdd(logKey, 0))
            {
                AppLog.Error("L10n", $"{action}：baseName={baseName}", ex);
            }
        }

        private static string ParseFeatureFromKey(string key)
        {
            // Key 约定：<Feature>_<Name>...
            // 若没有 '_'（历史遗留/特殊场景），统一归到 Common。
            int underscore = key.IndexOf('_');
            if (underscore <= 0)
            {
                return "Common";
            }

            return key.Substring(0, underscore);
        }

        private static bool TryGetString(string languageSegment, string feature, string key, out string? value)
        {
            value = null;
            ResourceManager manager = GetOrCreateResourceManager(languageSegment, feature);
            string baseName = $"WindBoard.Localization.{languageSegment}.{feature}";

            try
            {
                // 资源名已经包含语言段（例如 zh-CN 或 zh_CN），因此这里固定用 InvariantCulture 读取。
                value = manager.GetString(key, CultureInfo.InvariantCulture);
                return true;
            }
            catch (MissingManifestResourceException ex)
            {
                LogResourceErrorOnce(baseName, "资源缺失", ex);
                return false;
            }
            catch (Exception ex)
            {
                LogResourceErrorOnce(baseName, $"读取失败：key={key}", ex);
                return false;
            }
        }

        private static void TryPreloadResourceSet(string languageSegment, string feature)
        {
            string baseName = $"WindBoard.Localization.{languageSegment}.{feature}";
            ResourceManager manager = GetOrCreateResourceManager(languageSegment, feature);

            try
            {
                _ = manager.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: false);
            }
            catch (Exception ex)
            {
                LogResourceErrorOnce(baseName, "预加载失败", ex);
            }
        }

        private static ResourceManager GetOrCreateResourceManager(string languageSegment, string feature)
        {
            return ResourceManagers.GetOrAdd(
                (languageSegment, feature),
                static key =>
                {
                    // 资源基名规则：RootNamespace + 文件夹 + 文件名（不含扩展名）。
                    // 本项目约定：WindBoard.Localization.<Language>/<Feature>.resx
                    string baseName = $"WindBoard.Localization.{key.LanguageSegment}.{key.Feature}";
                    return new ResourceManager(baseName, typeof(L10n).Assembly);
                });
        }

        private static IEnumerable<string> EnumerateCultureFallbackSegments(CultureInfo uiCulture, ResourceIndex index, string defaultSegment)
        {
            // 说明：资源命名中可能把 culture 的 '-' 规范化为 '_'，因此每个 cultureName 都要尝试两个候选段。
            HashSet<string> yielded = new(StringComparer.Ordinal);

            for (CultureInfo current = uiCulture; current != CultureInfo.InvariantCulture; current = current.Parent)
            {
                if (string.IsNullOrWhiteSpace(current.Name))
                {
                    break;
                }

                foreach (string candidate in EnumerateLanguageSegmentCandidates(current.Name))
                {
                    if (index.LanguageSegments.Contains(candidate) && yielded.Add(candidate))
                    {
                        yield return candidate;
                    }
                }

                if (current.Parent == current)
                {
                    break;
                }
            }

            if (yielded.Add(defaultSegment))
            {
                yield return defaultSegment;
            }
        }

        private static bool HasAnyLanguageResources(ResourceIndex index, CultureInfo uiCulture, string defaultSegment)
        {
            for (CultureInfo current = uiCulture; current != CultureInfo.InvariantCulture; current = current.Parent)
            {
                if (string.IsNullOrWhiteSpace(current.Name))
                {
                    break;
                }

                foreach (string candidate in EnumerateLanguageSegmentCandidates(current.Name))
                {
                    if (candidate == defaultSegment)
                    {
                        continue;
                    }

                    if (index.LanguageSegments.Contains(candidate))
                    {
                        return true;
                    }
                }

                if (current.Parent == current)
                {
                    break;
                }
            }

            return false;
        }

        private static IEnumerable<string> EnumerateLanguageSegmentCandidates(string cultureName)
        {
            yield return cultureName;

            // 兼容：MSBuild 可能会把 '-' 规范化为 '_'。
            if (cultureName.Contains('-', StringComparison.Ordinal))
            {
                string normalized = cultureName.Replace('-', '_');
                if (!normalized.Equals(cultureName, StringComparison.Ordinal))
                {
                    yield return normalized;
                }
            }
        }

        private static string? GetDefaultLanguageSegment(ResourceIndex index)
        {
            foreach (string candidate in EnumerateLanguageSegmentCandidates(DefaultCultureName))
            {
                if (index.LanguageSegments.Contains(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static ResourceIndex BuildResourceIndex()
        {
            // 通过程序集嵌入的资源名建立索引：
            // - 避免每次缺资源都走 MissingManifestResourceException 的异常路径（性能与日志都更糟）。
            // - 同时也能自动适配资源名中 '-' vs '_' 的差异。
            Assembly assembly = typeof(L10n).Assembly;

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

                // 期望格式：WindBoard.Localization.<LanguageSegment>.<Feature>.resources
                string payload = name.Substring(prefix.Length, name.Length - prefix.Length - suffix.Length);
                int dot = payload.IndexOf('.');
                if (dot <= 0 || dot >= payload.Length - 1)
                {
                    continue;
                }

                string languageSegment = payload.Substring(0, dot);
                string feature = payload.Substring(dot + 1);

                index.LanguageSegments.Add(languageSegment);

                if (!index.FeaturesByLanguageSegment.TryGetValue(languageSegment, out HashSet<string>? features))
                {
                    features = new HashSet<string>(StringComparer.Ordinal);
                    index.FeaturesByLanguageSegment.Add(languageSegment, features);
                }

                features.Add(feature);
            }

            return index;
        }

        private sealed class ResourceIndex
        {
            internal HashSet<string> LanguageSegments { get; } = new(StringComparer.Ordinal);

            internal Dictionary<string, HashSet<string>> FeaturesByLanguageSegment { get; } = new(StringComparer.Ordinal);

            internal bool HasBase(string languageSegment, string feature)
            {
                return FeaturesByLanguageSegment.TryGetValue(languageSegment, out HashSet<string>? features) && features.Contains(feature);
            }

            internal IReadOnlyCollection<string> GetFeatures(string languageSegment)
            {
                if (FeaturesByLanguageSegment.TryGetValue(languageSegment, out HashSet<string>? features))
                {
                    return features;
                }

                return Array.Empty<string>();
            }
        }
    }
}
