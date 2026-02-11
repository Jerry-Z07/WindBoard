using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Resources;

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
        // 资源基名规则：RootNamespace + 文件夹 + 文件名（不含扩展名）。
        private static readonly ResourceManager ResourceManager = new("WindBoard.Localization.Strings", typeof(L10n).Assembly);

        // 每个缺失 Key 只打一次日志，避免 UI 高频取值刷屏。
        private static readonly ConcurrentDictionary<string, byte> MissingKeyLogged = new(StringComparer.Ordinal);

        /// <summary>
        /// 初始化入口（可选）。
        /// 目前用于尽早触发一次资源加载，便于在启动阶段就发现资源打包/命名错误。
        /// </summary>
        internal static void Initialize()
        {
            try
            {
                _ = ResourceManager.GetResourceSet(CultureInfo.CurrentUICulture, createIfNotExists: true, tryParents: true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[L10n] 初始化失败：{ex}");
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
                string? value = ResourceManager.GetString(key, CultureInfo.CurrentUICulture);
                if (!string.IsNullOrEmpty(value))
                {
                    return value;
                }

                LogMissingKeyOnce(key);
                return fallback ?? key;
            }
            catch (MissingManifestResourceException ex)
            {
                Debug.WriteLine($"[L10n] 资源缺失：key={key}, ex={ex}");
                return fallback ?? key;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[L10n] 读取失败：key={key}, ex={ex}");
                return fallback ?? key;
            }
        }

        /// <summary>
        /// 获取本地化字符串，并按当前文化进行格式化。
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
                Debug.WriteLine($"[L10n] 格式化失败：key={key}, template='{template}', ex={ex}");
                return template;
            }
        }

        private static void LogMissingKeyOnce(string key)
        {
            if (MissingKeyLogged.TryAdd(key, 0))
            {
                Debug.WriteLine($"[L10n] 缺少资源 key：{key}");
            }
        }
    }
}

