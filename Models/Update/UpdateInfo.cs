using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace WindBoard.Models.Update
{
    public sealed class UpdateInfo
    {
        [JsonProperty("version")]
        public string Version { get; set; } = string.Empty;

        [JsonProperty("versionName")]
        public string VersionName { get; set; } = string.Empty;

        [JsonProperty("releaseDate")]
        public DateTime ReleaseDate { get; set; }

        [JsonProperty("minSystemVersion")]
        public string MinSystemVersion { get; set; } = string.Empty;

        [JsonProperty("changelog")]
        public Dictionary<string, string> Changelog { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        [JsonProperty("assets")]
        public List<UpdateAsset> Assets { get; set; } = new();
    }
}

