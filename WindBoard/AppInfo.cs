using System;
using System.Reflection;

namespace WindBoard
{
    /// <summary>
    /// 应用信息（版本号等）。
    /// </summary>
    internal static class AppInfo
    {
        /// <summary>
        /// 版本号（建议用于日志/业务逻辑），例如：2.0.0 或 2.0.0-beta.1。
        /// </summary>
        internal static string Version { get; } = GetVersionCore();

        /// <summary>
        /// UI 展示版本号（带 v 前缀），例如：v2.0.0。
        /// </summary>
        internal static string DisplayVersion { get; } = GetDisplayVersionCore(Version);

        private static string GetVersionCore()
        {
            try
            {
                Assembly assembly = typeof(AppInfo).Assembly;

                // 优先使用 InformationalVersion（可承载语义版本与 CI metadata）。
                string? info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                if (!string.IsNullOrWhiteSpace(info))
                {
                    string value = info.Trim();

                    // 约定：UI/日志默认不展示 build metadata（例如：2.0.0+commit）。
                    int plusIndex = value.IndexOf('+', StringComparison.Ordinal);
                    if (plusIndex >= 0)
                    {
                        value = value.Substring(0, plusIndex);
                    }

                    return string.IsNullOrWhiteSpace(value) ? "unknown" : value;
                }

                // 兜底：AssemblyName.Version 通常为四段（2.0.0.0），这里折叠到三段更符合用户认知。
                Version? v = assembly.GetName().Version;
                if (v is not null)
                {
                    if (v.Revision == 0)
                    {
                        return $"{v.Major}.{v.Minor}.{Math.Max(0, v.Build)}";
                    }

                    return v.ToString();
                }
            }
            catch
            {
                // 忽略：版本号读取失败不应影响启动
            }

            return "unknown";
        }

        private static string GetDisplayVersionCore(string version)
        {
            if (string.IsNullOrWhiteSpace(version))
            {
                return "vunknown";
            }

            string v = version.Trim();
            if (v.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            {
                return v;
            }

            return "v" + v;
        }
    }
}
