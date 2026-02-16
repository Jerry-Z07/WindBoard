using System;
using System.Threading.Tasks;
using WindBoard.Logging;
using WindBoard.Updates;

namespace WindBoard
{
    public sealed partial class MainWindow
    {
        private bool _autoUpdateCheckStarted;

        private void TryStartAutoUpdateCheckOnce()
        {
            if (_autoUpdateCheckStarted)
            {
                return;
            }

            _autoUpdateCheckStarted = true;

            // 启动后延迟检查：降低对启动性能的影响，也避免与首次渲染/资源加载抢占。
            _ = AutoCheckUpdatesAfterDelayAsync();
        }

        private async Task AutoCheckUpdatesAfterDelayAsync()
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(4)).ConfigureAwait(false);
                await AppUpdateService.Instance.TryAutoCheckAndRemindAsync(this).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                // 忽略：窗口生命周期内取消（若未来接入取消信号）
            }
            catch (Exception ex)
            {
                // 自动检查失败不应影响主流程，但需要记录日志便于排查网络/解析问题。
                AppLog.Warn("Updates", "自动检查更新失败", ex);
            }
        }
    }
}

