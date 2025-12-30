using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace WindBoard.Models.Wbi
{
    /// <summary>
    /// WBI 文件清单
    /// </summary>
    public sealed class WbiManifest
    {
        /// <summary>格式版本</summary>
        [JsonProperty("version")]
        public string Version { get; set; } = "1.0";

        /// <summary>最低兼容版本</summary>
        [JsonProperty("min_compatible_version")]
        public string MinCompatibleVersion { get; set; } = "1.0";

        /// <summary>导出时的应用版本</summary>
        [JsonProperty("app_version")]
        public string? AppVersion { get; set; }

        /// <summary>创建时间</summary>
        [JsonProperty("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>页面数量</summary>
        [JsonProperty("page_count")]
        public int PageCount { get; set; }

        /// <summary>是否包含图片附件资源</summary>
        [JsonProperty("include_image_assets")]
        public bool IncludeImageAssets { get; set; }

        /// <summary>页面列表</summary>
        [JsonProperty("pages")]
        public List<WbiPageRef> Pages { get; set; } = new();
    }

    /// <summary>
    /// 页面引用（清单中的简要信息）
    /// </summary>
    public sealed class WbiPageRef
    {
        /// <summary>页面标识符</summary>
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;

        /// <summary>页码</summary>
        [JsonProperty("number")]
        public int Number { get; set; }
    }
}
