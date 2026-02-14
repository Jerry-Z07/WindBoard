using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using WindBoard.Logging;
using WindBoard.Reminders.Channels;

namespace WindBoard.Reminders
{
    /// <summary>
    /// 统一提醒服务：
    /// - 窗口化优先走 Windows Toast；
    /// - Toast 失败则降级为应用内右上角弹条；
    /// - 全屏模式（未来）：直接走右上角弹条（此处预留）。
    /// </summary>
    internal sealed class AppReminderService
    {
        private readonly object _gate = new();
        private readonly HashSet<string> _shownSignatures = new(StringComparer.Ordinal);

        private readonly IAppReminderChannel _toastChannel = new WindowsToastReminderChannel();
        private readonly IAppReminderChannel _bannerChannel = new InAppBannerReminderChannel();

        internal static AppReminderService Instance { get; } = new();

        private AppReminderService()
        {
        }

        internal void RemindOncePerSignature(Window window, string signature, AppReminderMessage message)
        {
            if (window is null)
            {
                throw new ArgumentNullException(nameof(window));
            }

            if (string.IsNullOrWhiteSpace(signature))
            {
                throw new ArgumentException("signature 不能为空", nameof(signature));
            }

            bool firstTime;
            lock (_gate)
            {
                firstTime = _shownSignatures.Add(signature);
            }

            if (!firstTime)
            {
                return;
            }

            bool isFullScreen = WindowDisplayModeHelper.IsFullScreen(window);
            if (isFullScreen)
            {
                // 预留：未来全屏时直接走应用内弹条（避免 Toast 遮挡或体验不一致）。
                if (!_bannerChannel.TryShow(window, message, out Exception? bannerError))
                {
                    AppLog.Warn("Reminders", "全屏提醒展示失败（应用内弹条通道不可用）", bannerError);
                }

                return;
            }

            if (_toastChannel.TryShow(window, message, out Exception? toastError))
            {
                AppLog.Info("Reminders", $"已发送 Windows 通知：signature='{signature}'");
                return;
            }

            AppLog.Warn("Reminders", "Windows 通知发送失败，已降级为应用内弹条", toastError);
            if (_bannerChannel.TryShow(window, message, out Exception? fallbackError))
            {
                AppLog.Info("Reminders", $"已展示应用内弹条：signature='{signature}'");
                return;
            }

            // 两种通道都失败：记录错误但不影响主流程。
            AppLog.Error("Reminders", "提醒展示失败：Windows 通知与应用内弹条均不可用", fallbackError);
        }
    }
}

