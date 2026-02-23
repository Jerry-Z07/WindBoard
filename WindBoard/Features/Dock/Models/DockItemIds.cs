using System.Collections.Generic;

namespace WindBoard.Features.Dock.Models
{
    /// <summary>
    /// Dock 内各功能的标识符（用于落盘与排序）。
    /// </summary>
    internal static class DockItemIds
    {
        // 左侧 Dock：窗口与入口
        public const string More = "more";
        public const string Minimize = "minimize";
        public const string Import = "import";

        // 中部 Dock：工具
        public const string ToolSelect = "toolSelect";
        public const string ToolPen = "toolPen";
        public const string ToolEraser = "toolEraser";

        // 中部 Dock：撤销/重做
        public const string Undo = "undo";
        public const string Redo = "redo";

        // 右侧 Dock：页面管理
        public const string PagePrev = "pagePrev";
        public const string PageIndicator = "pageIndicator";
        public const string PageNext = "pageNext";
        public const string PageAdd = "pageAdd";
    }

    internal static class DockSettingsDefaults
    {
        public static readonly IReadOnlyList<string> LeftOrder =
        [
            DockItemIds.More,
            DockItemIds.Minimize,
            DockItemIds.Import,
        ];

        public static readonly IReadOnlyList<string> ToolsOrder =
        [
            DockItemIds.ToolSelect,
            DockItemIds.ToolPen,
            DockItemIds.ToolEraser,
        ];

        public static readonly IReadOnlyList<string> UndoRedoOrder =
        [
            DockItemIds.Undo,
            DockItemIds.Redo,
        ];

        public static readonly IReadOnlyList<string> PagesOrder =
        [
            DockItemIds.PagePrev,
            DockItemIds.PageIndicator,
            DockItemIds.PageNext,
            DockItemIds.PageAdd,
        ];
    }
}

