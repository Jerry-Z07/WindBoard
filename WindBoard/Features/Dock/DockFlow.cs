using System;
using Microsoft.UI.Xaml;
using WindBoard.Features.Dock.Models;
using WindBoard.Features.Dock.Services;
using WindBoard.Logging;

namespace WindBoard.Features.Dock
{
    /// <summary>
    /// Dock 功能编排：负责读取 Dock 设置快照，并将其应用到主窗口 Dock 区域。
    /// </summary>
    internal sealed class DockFlow
    {
        private readonly Func<DockSettings> _getDockSettingsSnapshot;
        private readonly Func<XamlRoot?> _tryGetDialogXamlRoot;
        private readonly DockSettingsApplier _applier = new();

        internal DockFlow(Func<DockSettings> getDockSettingsSnapshot, Func<XamlRoot?> tryGetDialogXamlRoot)
        {
            _getDockSettingsSnapshot = getDockSettingsSnapshot ?? throw new ArgumentNullException(nameof(getDockSettingsSnapshot));
            _tryGetDialogXamlRoot = tryGetDialogXamlRoot ?? throw new ArgumentNullException(nameof(tryGetDialogXamlRoot));
        }

        internal void ApplyToMainWindow(DockMainWindowHost host)
        {
            if (host is null)
            {
                throw new ArgumentNullException(nameof(host));
            }

            DockSettings dock;
            try
            {
                dock = _getDockSettingsSnapshot();
            }
            catch (Exception ex)
            {
                AppLog.Error("Dock", "读取 Dock 设置失败。", ex);
                return;
            }

            try
            {
                _applier.ApplyToMainWindow(host, dock, _tryGetDialogXamlRoot);
            }
            catch (Exception ex)
            {
                // 兜底：避免异常冒泡到 UI 线程导致崩溃。
                AppLog.Error("Dock", "应用 Dock 设置到主界面失败。", ex);
            }
        }
    }
}

