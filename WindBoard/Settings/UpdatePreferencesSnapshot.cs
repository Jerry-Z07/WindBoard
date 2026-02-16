using System;

namespace WindBoard.Settings
{
    /// <summary>
    /// 更新偏好与状态的只读快照（供更新模块/启动流程使用）。
    /// </summary>
    internal sealed class UpdatePreferencesSnapshot
    {
        public required UpdateCheckInterval AutoCheckInterval { get; init; }

        public required DateTimeOffset? LastCheckUtc { get; init; }

        public required string LastNotifiedVersion { get; init; }
    }
}

