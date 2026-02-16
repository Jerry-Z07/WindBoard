using System;
using WindBoard.Settings;

namespace WindBoard.Updates
{
    /// <summary>
    /// 下载源测速触发策略（纯逻辑，便于单元测试）。
    /// </summary>
    internal static class DownloadSourceSpeedTestPolicy
    {
        internal static bool ShouldSpeedTest(
            AppInstallKind installKind,
            DownloadSourcePolicy policy,
            DateTimeOffset? lastTestUtc,
            UpdateCheckMode mode,
            DateTimeOffset nowUtc)
        {
            if (policy == DownloadSourcePolicy.Fixed)
            {
                return false;
            }

            // 安装版：安装后只需要测速一次（写入 settings）。
            if (installKind == AppInstallKind.Installer)
            {
                return lastTestUtc is null;
            }

            // 便携版：在“更新周期触发获取更新前”测速。
            // 这里以自动检查（Auto）作为“周期触发”的入口；手动检查不强制测速，避免阻塞 UI。
            if (installKind == AppInstallKind.Portable)
            {
                return mode == UpdateCheckMode.Auto;
            }

            // 兜底：未知安装形态下不强制测速，避免多余网络请求。
            return false;
        }
    }
}

