using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace WindBoard.Updates
{
    /// <summary>
    /// 对齐 release.yml 生成的 dist/latest.json 结构。
    /// </summary>
    internal sealed class LatestReleaseInfo
    {
        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;

        [JsonPropertyName("versionName")]
        public string VersionName { get; set; } = string.Empty;

        [JsonPropertyName("releaseDate")]
        public string ReleaseDate { get; set; } = string.Empty;

        /// <summary>
        /// 多语言更新日志（例如：zh-CN/en-US）。
        /// </summary>
        [JsonPropertyName("changelog")]
        public Dictionary<string, string>? Changelog { get; set; }

        [JsonPropertyName("assets")]
        public List<LatestReleaseAsset> Assets { get; set; } = new();
    }

    internal sealed class LatestReleaseAsset
    {
        /// <summary>
        /// 目标架构（x86/x64/arm64）。
        /// </summary>
        [JsonPropertyName("arch")]
        public string Arch { get; set; } = string.Empty;

        /// <summary>
        /// 工作流生成时填充的 runtime 标记（当前：self-contained/installer）。
        /// 注意：installer 里同时包含自包含与 -fd 变体，需要通过 fileName 再区分。
        /// </summary>
        [JsonPropertyName("runtime")]
        public string Runtime { get; set; } = string.Empty;

        [JsonPropertyName("fileName")]
        public string FileName { get; set; } = string.Empty;

        [JsonPropertyName("downloadUrl")]
        public string DownloadUrl { get; set; } = string.Empty;

        [JsonPropertyName("size")]
        public long Size { get; set; }

        [JsonPropertyName("sha256")]
        public string Sha256 { get; set; } = string.Empty;
    }
}

