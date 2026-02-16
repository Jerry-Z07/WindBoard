using System;

namespace WindBoard.Settings
{
    /// <summary>
    /// 下载源标识（用于对 GitHub 链接做镜像拼接）。
    /// </summary>
    internal enum DownloadSourceId
    {
        Github,
        GhProxy,
        Felicity,
        ZeroSeven,
    }

    /// <summary>
    /// 下载源标识解析与归一化（settings.json ⇄ 内存态）。
    /// </summary>
    internal static class DownloadSourceIdParser
    {
        internal const string GithubValue = "github";
        internal const string GhProxyValue = "gh-proxy";
        internal const string FelicityValue = "felicity";
        internal const string ZeroSevenValue = "07";

        internal static bool TryParse(string? text, out DownloadSourceId id)
        {
            id = DownloadSourceId.Github;

            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string value = text.Trim();

            if (value.Equals(GithubValue, StringComparison.OrdinalIgnoreCase))
            {
                id = DownloadSourceId.Github;
                return true;
            }

            if (value.Equals(GhProxyValue, StringComparison.OrdinalIgnoreCase))
            {
                id = DownloadSourceId.GhProxy;
                return true;
            }

            if (value.Equals(FelicityValue, StringComparison.OrdinalIgnoreCase))
            {
                id = DownloadSourceId.Felicity;
                return true;
            }

            if (value.Equals(ZeroSevenValue, StringComparison.OrdinalIgnoreCase))
            {
                id = DownloadSourceId.ZeroSeven;
                return true;
            }

            return false;
        }

        internal static string ToSettingValue(DownloadSourceId id)
        {
            return id switch
            {
                DownloadSourceId.GhProxy => GhProxyValue,
                DownloadSourceId.Felicity => FelicityValue,
                DownloadSourceId.ZeroSeven => ZeroSevenValue,
                _ => GithubValue,
            };
        }
    }
}

