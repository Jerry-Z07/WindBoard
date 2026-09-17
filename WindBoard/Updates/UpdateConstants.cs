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

        // Microsoft Store 产品 ID（Partner Center「Product identity」中的 Store ID）：
        // 单一配置点，供“更新由 Store 托管”卡片打开商店页面使用。
        // 占位值待 Partner Center 保留应用名并分配真实 Product ID 后替换（release.yml 中的
        // vars.MSIX_STORE_PRODUCT_ID 只影响发布说明里的链接，此处影响应用内跳转）。
        internal const string StoreProductId = "REPLACE_WITH_STORE_PRODUCT_ID";

        /// <summary>
        /// Store 应用协议链接（优先使用：直接打开 Store 应用的产品页）。
        /// </summary>
        internal static string GetStoreProductProtocolUrl(string productId)
        {
            return $"ms-windows-store://pdp/?ProductId={Uri.EscapeDataString(productId.Trim())}";
        }

        /// <summary>
        /// Store 网页链接（协议链接不可用时的降级入口）。
        /// </summary>
        internal static string GetStoreProductWebUrl(string productId)
        {
            return $"https://apps.microsoft.com/detail/{Uri.EscapeDataString(productId.Trim())}";
        }

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

