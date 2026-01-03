using Newtonsoft.Json;

namespace WindBoard.Models.Update
{
    public sealed class UpdateAsset
    {
        [JsonProperty("arch")]
        public string Arch { get; set; } = string.Empty; // "x86", "x64", "arm64"

        [JsonProperty("runtime")]
        public string Runtime { get; set; } = string.Empty; // "self-contained", "framework-dependent", "installer"

        [JsonProperty("fileName")]
        public string FileName { get; set; } = string.Empty;

        [JsonProperty("downloadUrl")]
        public string DownloadUrl { get; set; } = string.Empty;

        [JsonProperty("size")]
        public long Size { get; set; }

        [JsonProperty("sha256")]
        public string Sha256 { get; set; } = string.Empty;
    }
}

