using System;
using System.Collections.Generic;
using WindBoard.Settings;

namespace WindBoard.Updates
{
    /// <summary>
    /// GitHub 链接改写。
    /// Github 源保持原链接，镜像源为镜像前缀加原链接（例如 https://gh-proxy.top/https://github.com/...）。
    /// 支持 github.com 与 raw.githubusercontent.com 两种原链接。
    /// </summary>
    internal static class DownloadSourceUrlRewriter
    {
        internal const string GhProxyPrefix = "https://gh-proxy.top/";
        internal const string ZeroSevenPrefix = "https://ghm.078465.xyz/";

        private static readonly DownloadSourceId[] AllSourceIds =
        [
            DownloadSourceId.Github,
            DownloadSourceId.GhProxy,
            DownloadSourceId.ZeroSeven,
        ];

        internal static IReadOnlyList<DownloadSourceId> GetAllSourceIds()
        {
            return AllSourceIds;
        }

        internal static string Rewrite(string originalUrl, DownloadSourceId sourceId)
        {
            if (string.IsNullOrWhiteSpace(originalUrl))
            {
                return originalUrl ?? string.Empty;
            }

            string url = originalUrl.Trim();
            if (sourceId == DownloadSourceId.Github)
            {
                return url;
            }

            // 只对 GitHub 原链接做镜像拼接，避免把非 GitHub 链接误拼到镜像域名下。
            // 更新包走 github.com/release 附件，公告走 raw.githubusercontent.com。
            if (!IsMirrorableGithubUrl(url))
            {
                return url;
            }

            string prefix = GetPrefix(sourceId);
            return prefix + url;
        }

        private static bool IsMirrorableGithubUrl(string url)
        {
            return url.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("https://raw.githubusercontent.com/", StringComparison.OrdinalIgnoreCase);
        }

        internal static IReadOnlyList<DownloadSourceId> BuildFailoverOrder(DownloadSourceId preferred)
        {
            // 失败轮询切换顺序：优先使用 preferred，其余镜像源兜底，Github 原链接最后兜底（最稳定，但可能较慢）。
            var order = new List<DownloadSourceId>(capacity: 4);

            void Add(DownloadSourceId id)
            {
                if (!order.Contains(id))
                {
                    order.Add(id);
                }
            }

            Add(preferred);
            Add(DownloadSourceId.GhProxy);
            Add(DownloadSourceId.ZeroSeven);
            Add(DownloadSourceId.Github);

            return order;
        }

        private static string GetPrefix(DownloadSourceId id)
        {
            // 保证以 / 结尾，便于直接拼接原链接。
            string prefix = id switch
            {
                DownloadSourceId.GhProxy => GhProxyPrefix,
                DownloadSourceId.ZeroSeven => ZeroSevenPrefix,
                _ => string.Empty,
            };

            if (string.IsNullOrWhiteSpace(prefix))
            {
                return string.Empty;
            }

            // CA1865：单字符后缀检查使用 char 重载（ordinal 语义一致），避免字符串分配。
            return prefix.EndsWith('/') ? prefix : prefix + "/";
        }
    }
}

