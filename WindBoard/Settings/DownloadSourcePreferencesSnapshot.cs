using System;

namespace WindBoard.Settings
{
    /// <summary>
    /// 下载源偏好与测速状态的只读快照（供更新/下载模块使用）。
    /// </summary>
    internal sealed class DownloadSourcePreferencesSnapshot
    {
        public required DownloadSourcePolicy Policy { get; init; }

        public required DownloadSourceId SourceId { get; init; }

        public required DateTimeOffset? LastTestUtc { get; init; }
    }
}

