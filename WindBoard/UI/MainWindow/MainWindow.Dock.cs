using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using WindBoard.Features.Dock;
using WindBoard.Features.Dock.Models;
using WindBoard.Features.Dock.Services;
using WindBoard.Logging;
using WindBoard.Settings;

namespace WindBoard
{
    /// <summary>
    /// 主窗口：Dock 入口与背景色同步相关代码（具体逻辑在 Features/Dock）。
    /// </summary>
    public sealed partial class MainWindow
    {
        private DockFlow? _dockFlow;

        private static void UpdateCanvasBackgroundBrush(Color color)
        {
            // 页面管理缩略图等 XAML 视觉元素使用该资源刷子作为背景色；
            // 这里同步更新颜色，保证与 DirectX 渲染清屏色一致。
            if (Application.Current is null)
            {
                return;
            }

            if (Application.Current.Resources.TryGetValue("CanvasBackgroundBrush", out object? brushObj)
                && brushObj is SolidColorBrush brush)
            {
                brush.Color = color;
            }
        }

        private void ApplyDockSettingsToUi()
        {
            try
            {
                // 延迟初始化：避免字段初始值设定项捕获实例成员导致编译错误。
                _dockFlow ??= new DockFlow(
                    getDockSettingsSnapshot: () => AppSettingsService.Instance.GetDockSettingsSnapshot(),
                    tryGetDialogXamlRoot: TryGetDialogXamlRoot);

                _dockFlow.ApplyToMainWindow(CreateDockHost());
            }
            catch (Exception ex)
            {
                // 兜底：避免异常冒泡到 UI 线程导致崩溃。
                AppLog.Error("Dock", "应用 Dock 设置异常。", ex);
            }
        }

        private DockMainWindowHost CreateDockHost()
        {
            // 注意：这里是 MainWindow 与 Dock Feature 的桥接层，负责提供 Dock 所需的 UI 引用。
            return new DockMainWindowHost
            {
                LeftDockPanel = LeftDockPanel,
                LeftDockElementsById = new Dictionary<string, UIElement>(StringComparer.Ordinal)
                {
                    [DockItemIds.More] = MoreButton,
                    [DockItemIds.Minimize] = MinimizeButton,
                    [DockItemIds.Import] = ImportButton,
                },

                ToolsDockPanel = ToolsDockPanel,
                ToolsDockElementsById = new Dictionary<string, UIElement>(StringComparer.Ordinal)
                {
                    [DockItemIds.ToolSelect] = SelectToolToggleButton,
                    [DockItemIds.ToolPen] = PenToolToggleButton,
                    [DockItemIds.ToolEraser] = EraserToggleButton,
                },

                UndoRedoDockPanel = UndoRedoDockPanel,
                UndoRedoDockElementsById = new Dictionary<string, UIElement>(StringComparer.Ordinal)
                {
                    [DockItemIds.Undo] = UndoButton,
                    [DockItemIds.Redo] = RedoButton,
                },
                UndoRedoSeparator = UndoRedoSeparator,

                PagesDockPanel = PagesDockPanel,
                PagesDockElementsById = new Dictionary<string, UIElement>(StringComparer.Ordinal)
                {
                    [DockItemIds.PagePrev] = PagePrevButton,
                    [DockItemIds.PageIndicator] = PageIndicatorButton,
                    [DockItemIds.PageNext] = PageNextButton,
                    [DockItemIds.PageAdd] = AddButton,
                },

                LeftShortcutDockContainer = LeftShortcutDockContainer,
                RightShortcutDockContainer = RightShortcutDockContainer,
                LeftShortcutDockPanel = LeftShortcutDockPanel,
                RightShortcutDockPanel = RightShortcutDockPanel,
            };
        }
    }
}
