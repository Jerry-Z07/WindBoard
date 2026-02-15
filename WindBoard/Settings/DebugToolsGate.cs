using System;
using System.Threading;
using WindBoard.Logging;

namespace WindBoard.Settings
{
    /// <summary>
    /// 调试入口开关（会话级，不落盘）。
    /// 
    /// 约定：
    /// - Debug 构建：入口始终显示（无需解锁）。
    /// - Release 构建：默认隐藏；可在运行时解锁本次会话显示。
    /// </summary>
    internal static class DebugToolsGate
    {
#if DEBUG
        internal static bool IsVisible => true;
#else
        private static int _isVisible;

        internal static bool IsVisible => Volatile.Read(ref _isVisible) != 0;
#endif

        /// <summary>
        /// 状态变更事件：用于通知 SettingsWindow 刷新导航项显隐。
        /// </summary>
        internal static event EventHandler? Changed;

        internal static void UnlockForSession()
        {
#if DEBUG
            // Debug 构建下入口默认显示，无需解锁。
            return;
#else
            if (Interlocked.Exchange(ref _isVisible, 1) != 0)
            {
                return;
            }

            AppLog.Info("Debug", "调试入口已解锁（仅本次会话）");
            TryRaiseChanged();
#endif
        }

        internal static void LockForSession()
        {
#if DEBUG
            // Debug 构建下入口默认显示，不支持锁回去，避免误操作造成困惑。
            return;
#else
            if (Interlocked.Exchange(ref _isVisible, 0) == 0)
            {
                return;
            }

            AppLog.Info("Debug", "调试入口已隐藏（仅本次会话）");
            TryRaiseChanged();
#endif
        }

        private static void TryRaiseChanged()
        {
            try
            {
                Changed?.Invoke(null, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                // 防御：订阅方异常不应影响主流程。
                AppLog.Warn("Debug", "广播调试入口状态变更失败", ex);
            }
        }
    }
}

