using System;
using System.IO;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using WindBoard.Features.Camouflage.Models;
using WindBoard.Features.Camouflage.Services;
using WindBoard.Logging;
using WindBoard.Settings;

namespace WindBoard.Features.Camouflage
{
    /// <summary>
    /// Camouflage 功能编排：
    /// - 将伪装设置应用到窗口标题/图标
    /// - 在设置发生变化时自动更新一次桌面快捷方式（启动时不自动生成）
    /// </summary>
    internal sealed class CamouflageFlow : IDisposable
    {
        private readonly DispatcherQueue _dispatcherQueue;
        private readonly Func<IntPtr> _tryGetHwnd;
        private readonly Action<string> _setWindowTitle;

        private DispatcherQueueTimer? _shortcutUpdateTimer;
        private CamouflageShortcutUpdateRequest? _pendingShortcutUpdate;
        private string _lastSettingsSignature = string.Empty;
        private bool _hasAppliedSnapshot;
        private string? _defaultIconCachePath;
        private string? _lastAppliedWindowTitle;
        private string? _lastAppliedWindowIconPath;

        private sealed class CamouflageShortcutUpdateRequest
        {
            public string Signature { get; }
            public string Title { get; }
            public string? IconPath { get; }
            public bool Enabled { get; }

            public CamouflageShortcutUpdateRequest(string signature, string title, string? iconPath, bool enabled)
            {
                Signature = signature;
                Title = title;
                IconPath = iconPath;
                Enabled = enabled;
            }
        }

        internal CamouflageFlow(DispatcherQueue dispatcherQueue, Func<IntPtr> tryGetHwnd, Action<string> setWindowTitle)
        {
            _dispatcherQueue = dispatcherQueue ?? throw new ArgumentNullException(nameof(dispatcherQueue));
            _tryGetHwnd = tryGetHwnd ?? throw new ArgumentNullException(nameof(tryGetHwnd));
            _setWindowTitle = setWindowTitle ?? throw new ArgumentNullException(nameof(setWindowTitle));
        }

        public void Dispose()
        {
            // 说明：DispatcherQueueTimer 不要求显式 Dispose，这里统一 stop + 解除 pending，
            // 避免在窗口关闭后仍触发 Tick 访问 AppSettingsService。
            CancelPendingShortcutUpdate();

            if (_shortcutUpdateTimer is not null)
            {
                _shortcutUpdateTimer.Tick -= OnShortcutUpdateTimerTick;
                _shortcutUpdateTimer = null;
            }
        }

        internal void ApplyToWindow()
        {
            // 首次应用时只负责同步窗口标题/图标，不自动生成/更新桌面快捷方式。
            bool isStartup = !_hasAppliedSnapshot;
            _hasAppliedSnapshot = true;

            CamouflageSettingsSnapshot snapshot = AppSettingsService.Instance.GetCamouflageSettingsSnapshot();
            string defaultTitle = global::WindBoard.AppDisplayName.Get();

            CamouflageResult result = CamouflageService.Instance.BuildResult(snapshot, defaultTitle);

            TryApplyWindowTitle(result.Title);
            TryApplyWindowIcon(result);

            // 桌面快捷方式：仅在设置“发生修改”时自动更新一次；每次启动不再自动生成。
            // 说明：BuildResult 可能触发“缓存重建并写回设置”，因此这里要重新取一次快照用于签名。
            CamouflageSettingsSnapshot signatureSnapshot = AppSettingsService.Instance.GetCamouflageSettingsSnapshot();
            string currentSignature = CamouflageService.Instance.GetCamouflageShortcutSettingsSignature(signatureSnapshot);
            if (isStartup)
            {
                _lastSettingsSignature = currentSignature;
                return;
            }

            TryScheduleShortcutUpdate(result, signatureSnapshot, currentSignature);
        }

        private void TryApplyWindowTitle(string title)
        {
            if (string.Equals(_lastAppliedWindowTitle, title, StringComparison.Ordinal))
            {
                return;
            }

            _lastAppliedWindowTitle = title;

            try
            {
                _setWindowTitle(title);
            }
            catch (Exception ex)
            {
                // 忽略设置标题失败：不影响主流程
                AppLog.Warn("Camouflage", "设置窗口标题失败（Window.Title）", ex);
            }

            try
            {
                AppWindow? appWindow = TryGetAppWindow();
                if (appWindow is not null)
                {
                    appWindow.Title = title;
                }
            }
            catch (Exception ex)
            {
                // 忽略设置失败：不影响主流程
                AppLog.Warn("Camouflage", "设置窗口标题失败（AppWindow.Title）", ex);
            }
        }

        private void TryApplyWindowIcon(CamouflageResult result)
        {
            string? iconPathToSet = null;

            if (result.Enabled
                && !string.IsNullOrWhiteSpace(result.IconPath)
                && File.Exists(result.IconPath))
            {
                iconPathToSet = result.IconPath;
            }
            else
            {
                iconPathToSet = TryGetDefaultIconCachePath();
            }

            if (string.IsNullOrWhiteSpace(iconPathToSet))
            {
                return;
            }

            AppWindow? appWindow = TryGetAppWindow();
            if (appWindow is null)
            {
                // Window 句柄可能尚未就绪：不记录“已应用”，等待下一次时机再重试。
                return;
            }

            if (string.Equals(_lastAppliedWindowIconPath, iconPathToSet, StringComparison.Ordinal))
            {
                return;
            }

            try
            {
                appWindow.SetIcon(iconPathToSet);
                _lastAppliedWindowIconPath = iconPathToSet;
            }
            catch (Exception ex)
            {
                AppLog.Warn("Camouflage", $"设置窗口图标失败：path='{iconPathToSet}'", ex);
            }
        }

        private string? TryGetDefaultIconCachePath()
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(_defaultIconCachePath) && File.Exists(_defaultIconCachePath))
                {
                    return _defaultIconCachePath;
                }

                if (CamouflageService.Instance.TryEnsureDefaultIconCache(out string cachePath) && File.Exists(cachePath))
                {
                    _defaultIconCachePath = cachePath;
                    return cachePath;
                }

                // 兜底：如果 AppWindow.SetIcon 支持 exe/dll，可以尝试直接传 exe 路径。
                string? exePath = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath))
                {
                    return exePath;
                }
            }
            catch
            {
                return null;
            }

            return null;
        }

        private AppWindow? TryGetAppWindow()
        {
            try
            {
                IntPtr hwnd = _tryGetHwnd();
                if (hwnd == IntPtr.Zero)
                {
                    return null;
                }

                var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
                return AppWindow.GetFromWindowId(windowId);
            }
            catch
            {
                return null;
            }
        }

        private DispatcherQueueTimer GetOrCreateShortcutUpdateTimer()
        {
            if (_shortcutUpdateTimer is not null)
            {
                return _shortcutUpdateTimer;
            }

            _shortcutUpdateTimer = _dispatcherQueue.CreateTimer();
            _shortcutUpdateTimer.Interval = TimeSpan.FromMilliseconds(600);
            _shortcutUpdateTimer.IsRepeating = false;
            _shortcutUpdateTimer.Tick += OnShortcutUpdateTimerTick;
            return _shortcutUpdateTimer;
        }

        private void CancelPendingShortcutUpdate()
        {
            _pendingShortcutUpdate = null;
            _shortcutUpdateTimer?.Stop();
        }

        private void TryScheduleShortcutUpdate(
            CamouflageResult currentResult,
            CamouflageSettingsSnapshot snapshot,
            string currentSignature)
        {
            if (string.Equals(currentSignature, _lastSettingsSignature, StringComparison.Ordinal))
            {
                return;
            }

            _lastSettingsSignature = currentSignature;

            string lastGeneratedSignature = snapshot.ShortcutLastGeneratedSignature;
            if (string.Equals(lastGeneratedSignature, currentSignature, StringComparison.Ordinal))
            {
                // 用户把设置改回“已生成过”的状态：取消任何待执行的更新，避免生成旧配置的快捷方式。
                CancelPendingShortcutUpdate();
                return;
            }

            _pendingShortcutUpdate = new CamouflageShortcutUpdateRequest(
                currentSignature,
                currentResult.Title,
                currentResult.IconPath,
                currentResult.Enabled);

            DispatcherQueueTimer timer = GetOrCreateShortcutUpdateTimer();
            timer.Stop();
            timer.Start();
        }

        private void OnShortcutUpdateTimerTick(DispatcherQueueTimer sender, object args)
        {
            sender.Stop();

            CamouflageShortcutUpdateRequest? pending = _pendingShortcutUpdate;
            if (pending is null)
            {
                return;
            }

            CamouflageSettingsSnapshot snapshot = AppSettingsService.Instance.GetCamouflageSettingsSnapshot();
            string currentSignature = CamouflageService.Instance.GetCamouflageShortcutSettingsSignature(snapshot);
            if (!string.Equals(currentSignature, pending.Signature, StringComparison.Ordinal))
            {
                // 待更新签名已过期（期间设置又变化了并触发了新一轮调度），本次不再执行。
                return;
            }

            if (string.Equals(snapshot.ShortcutLastGeneratedSignature, pending.Signature, StringComparison.Ordinal))
            {
                return;
            }

            bool ok = CamouflageService.Instance.TryUpdateDesktopShortcut(
                pending.Title,
                pending.IconPath,
                pending.Enabled,
                snapshot.ShortcutLastGeneratedPath,
                out string shortcutPath,
                out string? errorMessage);

            if (!ok)
            {
                AppLog.Warn("Camouflage", $"自动更新桌面快捷方式失败：{errorMessage}");
                return;
            }

            AppSettingsService.Instance.Update(s =>
            {
                s.General.Camouflage.ShortcutLastGeneratedSignature = pending.Signature;
                s.General.Camouflage.ShortcutLastGeneratedPath = shortcutPath;
            });
            _pendingShortcutUpdate = null;
        }
    }
}
