using System;

namespace WindBoard.Updates
{
    /// <summary>
    /// 更新相关常量与 URL 构造。
    /// </summary>
    internal static class UpdateConstants
    {
        // 说明：当前更新源与发布工作流强绑定（GitHub Releases 的 latest.json）。
        // 如需迁移到自建更新源，可在此处集中修改。
        internal const string RepoOwner = "Jerry-Z07";
        internal const string RepoName = "WindBoard";

        internal static string LatestJsonUrl =>
            $"https://github.com/{RepoOwner}/{RepoName}/releases/latest/download/latest.json";

        internal static string ReleaseLatestPageUrl =>
            $"https://github.com/{RepoOwner}/{RepoName}/releases/latest";

        internal static string GetReleaseTagPageUrl(string? versionName)
        {
            if (string.IsNullOrWhiteSpace(versionName))
            {
                return ReleaseLatestPageUrl;
            }

            // GitHub tag 页面：/releases/tag/{tag}
            string tag = versionName.Trim();
            return $"https://github.com/{RepoOwner}/{RepoName}/releases/tag/{Uri.EscapeDataString(tag)}";
        }
    }
}

