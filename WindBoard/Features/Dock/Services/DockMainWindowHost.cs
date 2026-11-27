using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WindBoard.Features.Dock.Services
{
    /// <summary>
    /// 主窗口 Dock 区域的 UI 引用集合：用于让 Dock 功能在不直接依赖 MainWindow 的情况下操作 UI。
    /// </summary>
    internal sealed class DockMainWindowHost
    {
        public required StackPanel LeftDockPanel { get; init; }
        public required IReadOnlyDictionary<string, UIElement> LeftDockElementsById { get; init; }

        public required StackPanel ToolsDockPanel { get; init; }
        public required IReadOnlyDictionary<string, UIElement> ToolsDockElementsById { get; init; }

        public required StackPanel UndoRedoDockPanel { get; init; }
        public required IReadOnlyDictionary<string, UIElement> UndoRedoDockElementsById { get; init; }
        public required FrameworkElement UndoRedoSeparator { get; init; }

        public required StackPanel PagesDockPanel { get; init; }
        public required IReadOnlyDictionary<string, UIElement> PagesDockElementsById { get; init; }

        /// <summary>主 Dock 底板：无可见项时隐藏，避免留下空药丸。</summary>
        public required FrameworkElement MainDockBackground { get; init; }

        /// <summary>主 Dock 内容面板：无可见项时隐藏。</summary>
        public required FrameworkElement MainDockContentPanel { get; init; }

        /// <summary>右侧页面 Dock 容器：全部页面项隐藏时整体隐藏。</summary>
        public required FrameworkElement PagesDockContainer { get; init; }

        public required FrameworkElement LeftShortcutDockContainer { get; init; }
        public required FrameworkElement RightShortcutDockContainer { get; init; }
        public required StackPanel LeftShortcutDockPanel { get; init; }
        public required StackPanel RightShortcutDockPanel { get; init; }
    }
}

