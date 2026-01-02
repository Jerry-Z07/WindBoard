using System;
using System.Reflection;

namespace WindBoard.Services
{
    public static class AppVersionInfo
    {
        private static readonly Lazy<string> _version = new(GetVersion, isThreadSafe: true);

        public static string Version => _version.Value;

        private static string GetVersion()
        {
            try
            {
                Assembly assembly = typeof(AppVersionInfo).Assembly;

                string? infoVersion = assembly
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                    ?.InformationalVersion;

                if (!string.IsNullOrWhiteSpace(infoVersion))
                {
                    return infoVersion.Trim();
                }

                return assembly.GetName().Version?.ToString() ?? "0.0.0";
            }
            catch
            {
                return "0.0.0";
            }
        }
    }
}

