using System;
using System.Diagnostics;
using System.Reflection;

namespace WindBoard.Services
{
    public static class AppVersionInfo
    {
        private readonly record struct VersionInfo(string Version, System.Version? ParsedVersion, bool IsFallback);

        private static readonly Lazy<VersionInfo> _versionInfo = new(BuildVersionInfo, isThreadSafe: true);

        public static string Version => _versionInfo.Value.Version;

        public static System.Version? ParsedVersion => _versionInfo.Value.ParsedVersion;

        public static bool IsFallback => _versionInfo.Value.IsFallback;

        public static string? VersionOrNull => IsFallback ? null : Version;

        private static VersionInfo BuildVersionInfo()
        {
            try
            {
                Assembly assembly = typeof(AppVersionInfo).Assembly;

                string? infoVersion = assembly
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                    ?.InformationalVersion;

                if (!string.IsNullOrWhiteSpace(infoVersion))
                {
                    string trimmedInfoVersion = infoVersion.Trim();
                    return new VersionInfo(trimmedInfoVersion, TryParseVersion(trimmedInfoVersion), IsFallback: false);
                }

                System.Version? assemblyVersion = assembly.GetName().Version;
                if (assemblyVersion != null)
                {
                    return new VersionInfo(assemblyVersion.ToString(), assemblyVersion, IsFallback: false);
                }

                return new VersionInfo("0.0.0", ParsedVersion: null, IsFallback: true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AppVersion] Failed to resolve app version: {ex}");
                return new VersionInfo("0.0.0", ParsedVersion: null, IsFallback: true);
            }
        }

        private static System.Version? TryParseVersion(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            string trimmed = text.Trim();
            if (trimmed.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                trimmed = trimmed[1..];

            int separatorIndex = trimmed.IndexOfAny(new[] { '-', '+' });
            if (separatorIndex > 0)
                trimmed = trimmed[..separatorIndex];

            return System.Version.TryParse(trimmed, out System.Version? version) ? version : null;
        }
    }
}
