using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Windows.ApplicationModel.Resources;
using WindBoard.Logging;

namespace WindBoard.Localization
{
    /// <summary>
    /// 本地化字符串读取入口（基于 .resw / MRT Core）。
    ///
    /// 设计说明：
    /// - 外部 API 继续保持 <c>L10n.Get/Format</c> 与 <c>LocExtension</c> 不变，避免大面积调用点改动；
    /// - 底层改为读取 <c>WindBoard.pri</c> 中的 <c>.resw</c> 资源，适配 WinUI 3 的资源体系；
    /// - 缺失 Key / 缺翻译不应导致崩溃：回退为 fallback（或 key 本身），并仅记录一次日志，便于排查漏配。
    /// </summary>
    internal static class L10n
    {
        private const string DefaultCultureName = "zh-CN";
        private const string AppPriFileName = "WindBoard.pri";

        // 每个缺失 Key 只打一次日志，避免 UI 高频取值刷屏。
        private static readonly ConcurrentDictionary<string, byte> MissingKeyLogged = new(StringComparer.Ordinal);

        // 缺翻译日志：按 culture+key 去重（仅在“该 culture 已部分提供资源”时才会记录）。
        private static readonly ConcurrentDictionary<string, byte> MissingTranslationLogged = new(StringComparer.Ordinal);

        // 不支持的语言：按 culture 去重，避免整套 UI 取值时刷屏。
        private static readonly ConcurrentDictionary<string, byte> UnsupportedCultureLogged = new(StringComparer.Ordinal);

        // 资源加载/读取异常：按 action+name 去重，避免异常高频时刷屏。
        private static readonly ConcurrentDictionary<string, byte> ResourceErrorLogged = new(StringComparer.Ordinal);

        private static readonly Lazy<ResourceManager> Manager = new(CreateResourceManager, isThreadSafe: true);
        private static readonly ConcurrentDictionary<string, ResourceMap> FeatureMaps = new(StringComparer.Ordinal);

        /// <summary>
        /// 初始化入口（可选）。
        /// 目前用于尽早触发一次资源加载，便于在启动阶段就发现 PRI 打包或资源命名问题。
        /// </summary>
        internal static void Initialize()
        {
            try
            {
                if (!L10nResourceMetadata.HasCulture(DefaultCultureName))
                {
                    AppLog.Error("L10n", $"未找到默认语言资源：{DefaultCultureName}");
                    return;
                }

                IReadOnlyCollection<string> defaultFeatures = L10nResourceMetadata.GetFeatures(DefaultCultureName);
                if (defaultFeatures.Count == 0)
                {
                    AppLog.Error("L10n", $"默认语言资源为空：culture={DefaultCultureName}");
                    return;
                }

                _ = Manager.Value;

                foreach (string feature in defaultFeatures)
                {
                    TryPreloadFeatureMap(feature);
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("L10n", "初始化失败", ex);
            }
        }

        /// <summary>
        /// 获取当前程序包内“已提供资源”的语言列表（不包含 "system"）。
        /// </summary>
        internal static IReadOnlyList<string> GetSupportedCultureNames()
        {
            try
            {
                return L10nResourceMetadata.SupportedCultureNames;
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
        internal static string Get(string key, string? fallback = null)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return fallback ?? string.Empty;
            }

            try
            {
                if (!L10nResourceMetadata.HasCulture(DefaultCultureName))
                {
                    LogResourceErrorOnce(AppPriFileName, "默认语言资源缺失");
                    return fallback ?? key;
                }

                string feature = ParseFeatureFromKey(key);
                CultureInfo uiCulture = CultureInfo.CurrentUICulture;
                string requestedCultureName = NormalizeCultureName(uiCulture.Name) ?? uiCulture.Name;
                bool hasAnyRequestedLanguageResources = HasAnyLanguageResources(uiCulture);

                foreach (string cultureName in EnumerateCultureFallbackCultureNames(uiCulture))
                {
                    if (!L10nResourceMetadata.HasFeature(cultureName, feature))
                    {
                        continue;
                    }

                    if (!TryGetString(cultureName, feature, key, out string? value, out string? resolvedCultureName))
                    {
                        continue;
                    }

                    if (!string.IsNullOrEmpty(value))
                    {
                        if (string.Equals(resolvedCultureName, DefaultCultureName, StringComparison.OrdinalIgnoreCase)
                            && !string.IsNullOrWhiteSpace(requestedCultureName)
                            && !requestedCultureName.Equals(DefaultCultureName, StringComparison.OrdinalIgnoreCase))
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
            _ = UnsupportedCultureLogged.TryAdd(cultureName, 0);
        }

        private static void LogResourceErrorOnce(string name, string action, Exception? ex = null)
        {
            string logKey = $"{action}:{name}";
            if (ResourceErrorLogged.TryAdd(logKey, 0))
            {
                AppLog.Error("L10n", $"{action}：name={name}", ex);
            }
        }

        private static string ParseFeatureFromKey(string key)
        {
            int underscore = key.IndexOf('_');
            if (underscore <= 0)
            {
                return "Common";
            }

            return key.Substring(0, underscore);
        }

        private static ResourceManager CreateResourceManager()
        {
            return new ResourceManager(AppPriFileName);
        }

        private static bool TryGetString(string cultureName, string feature, string key, out string? value, out string? resolvedCultureName)
        {
            value = null;
            resolvedCultureName = null;

            try
            {
                ResourceMap featureMap = GetOrCreateFeatureMap(feature);
                ResourceContext context = CreateResourceContext(cultureName);
                ResourceCandidate? candidate = featureMap.TryGetValue(key, context);
                if (candidate is null)
                {
                    return true;
                }

                string? candidateCultureName = GetCandidateLanguageName(candidate);
                if (!IsLanguageCandidateMatch(candidateCultureName, cultureName))
                {
                    return true;
                }

                value = candidate.ValueAsString;
                resolvedCultureName = NormalizeCultureName(candidateCultureName) ?? cultureName;
                return true;
            }
            catch (Exception ex)
            {
                LogResourceErrorOnce($"{feature}/{key}", $"读取失败：culture={cultureName}", ex);
                return false;
            }
        }

        private static void TryPreloadFeatureMap(string feature)
        {
            try
            {
                _ = GetOrCreateFeatureMap(feature);
            }
            catch (Exception ex)
            {
                LogResourceErrorOnce(feature, "预加载失败", ex);
            }
        }

        private static ResourceMap GetOrCreateFeatureMap(string feature)
        {
            return FeatureMaps.GetOrAdd(feature, featureName => Manager.Value.MainResourceMap.GetSubtree(featureName));
        }

        private static ResourceContext CreateResourceContext(string cultureName)
        {
            ResourceContext context = Manager.Value.CreateResourceContext();
            context.QualifierValues["Language"] = cultureName;
            return context;
        }

        private static IEnumerable<string> EnumerateCultureFallbackCultureNames(CultureInfo uiCulture)
        {
            HashSet<string> yielded = new(StringComparer.OrdinalIgnoreCase);

            for (CultureInfo current = uiCulture; current != CultureInfo.InvariantCulture; current = current.Parent)
            {
                if (string.IsNullOrWhiteSpace(current.Name))
                {
                    break;
                }

                string candidate = NormalizeCultureName(current.Name) ?? current.Name;
                if (yielded.Add(candidate))
                {
                    yield return candidate;
                }

                if (current.Parent == current)
                {
                    break;
                }
            }

            if (yielded.Add(DefaultCultureName))
            {
                yield return DefaultCultureName;
            }
        }

        private static bool HasAnyLanguageResources(CultureInfo uiCulture)
        {
            foreach (string cultureName in EnumerateCultureFallbackCultureNames(uiCulture))
            {
                if (!cultureName.Equals(DefaultCultureName, StringComparison.OrdinalIgnoreCase)
                    && L10nResourceMetadata.HasCulture(cultureName))
                {
                    return true;
                }
            }

            return false;
        }

        private static string? GetCandidateLanguageName(ResourceCandidate candidate)
        {
            if (candidate.QualifierValues.TryGetValue("Language", out string? cultureName)
                && !string.IsNullOrWhiteSpace(cultureName))
            {
                return cultureName;
            }

            return null;
        }

        private static bool IsLanguageCandidateMatch(string? candidateCultureName, string requestedCultureName)
        {
            string? normalizedCandidate = NormalizeCultureName(candidateCultureName);
            if (string.IsNullOrWhiteSpace(normalizedCandidate))
            {
                return requestedCultureName.Equals(DefaultCultureName, StringComparison.OrdinalIgnoreCase);
            }

            string normalizedRequested = NormalizeCultureName(requestedCultureName) ?? requestedCultureName;
            return normalizedCandidate.Equals(normalizedRequested, StringComparison.OrdinalIgnoreCase);
        }

        private static string? NormalizeCultureName(string? cultureName)
        {
            if (string.IsNullOrWhiteSpace(cultureName))
            {
                return null;
            }

            string candidate = cultureName.Replace('_', '-');
            try
            {
                return CultureInfo.GetCultureInfo(candidate).Name;
            }
            catch
            {
                return candidate;
            }
        }
    }
}
