using System;
using System.Collections.Generic;
using System.Threading;
using WindBoard.Settings;

namespace WindBoard.Updates
{
    internal sealed class DownloadRequest
    {
        public string OriginalUrl { get; init; } = string.Empty;

        public string DestinationPath { get; init; } = string.Empty;

        public DownloadSourceId PreferredSourceId { get; init; } = DownloadSourceId.Github;

        public IReadOnlyList<DownloadSourceId>? FailoverOrder { get; init; }

        /// <summary>
        /// 最多轮询次数（每轮会按 FailoverOrder 依次尝试）。
        /// </summary>
        public int MaxCycles { get; init; } = 2;

        /// <summary>
        /// 空闲超时：一段时间无任何下载进度则视为“卡死”，主动取消并切换源。
        /// 
        /// 说明：
        /// - 默认 20 秒；
        /// - 设为 <see cref="Timeout.InfiniteTimeSpan"/> 或 &lt;= 0 表示禁用空闲超时。
        /// </summary>
        public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromSeconds(20);
    }
}

