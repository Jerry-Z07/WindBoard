using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;
using WindBoard.Board.Editing;
using WindBoard.Interaction;
using WindBoard.Settings;

namespace WindBoard
{
    /// <summary>
    /// 主窗口：Dock 配置应用与背景色同步相关代码。
    /// </summary>
    public sealed partial class MainWindow
    {
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
            DockSettings dock = AppSettingsService.Instance.GetDockSettingsSnapshot();

            ApplyDockOrder(
                LeftDockPanel,
                dock.LeftOrder,
                new Dictionary<string, UIElement>(StringComparer.Ordinal)
                {
                    [DockItemIds.More] = MoreButton,
                    [DockItemIds.Minimize] = MinimizeButton,
                    [DockItemIds.Import] = ImportButton,
                });

            ApplyDockOrder(
                ToolsDockPanel,
                dock.ToolsOrder,
                new Dictionary<string, UIElement>(StringComparer.Ordinal)
                {
                    [DockItemIds.ToolSelect] = SelectToolToggleButton,
                    [DockItemIds.ToolPen] = PenToolToggleButton,
                    [DockItemIds.ToolEraser] = EraserToggleButton,
                });

            ApplyDockOrder(
                UndoRedoDockPanel,
                dock.UndoRedoOrder,
                new Dictionary<string, UIElement>(StringComparer.Ordinal)
                {
                    [DockItemIds.Undo] = UndoButton,
                    [DockItemIds.Redo] = RedoButton,
                });

            ApplyDockOrder(
                PagesDockPanel,
                dock.PagesOrder,
                new Dictionary<string, UIElement>(StringComparer.Ordinal)
                {
                    [DockItemIds.PagePrev] = PagePrevButton,
                    [DockItemIds.PageIndicator] = PageIndicatorButton,
                    [DockItemIds.PageNext] = PageNextButton,
                    [DockItemIds.PageAdd] = AddButton,
                });

            Visibility undoRedoVisibility = dock.IsUndoRedoVisible ? Visibility.Visible : Visibility.Collapsed;
            UndoRedoSeparator.Visibility = undoRedoVisibility;
            UndoRedoDockPanel.Visibility = undoRedoVisibility;
        }

        private static void ApplyDockOrder(
            StackPanel panel,
            IReadOnlyList<string> order,
            IReadOnlyDictionary<string, UIElement> elementsById)
        {
            // 说明：Dock 的元素在 XAML 中是命名控件，这里仅调整它们在面板中的顺序，不创建/销毁控件。
            // 归一化已保证 order 只包含合法项并补齐缺失项，这里按 order 进行重排即可。
            panel.Children.Clear();

            foreach (string id in order)
            {
                if (elementsById.TryGetValue(id, out UIElement? element))
                {
                    panel.Children.Add(element);
                }
            }
        }

    }
}
