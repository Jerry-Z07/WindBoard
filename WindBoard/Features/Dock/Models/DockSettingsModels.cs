using System.Collections.Generic;

namespace WindBoard.Features.Dock.Models
{
    /// <summary>
    /// 快捷入口：放置位置。
    /// </summary>
    internal static class ShortcutDockSides
    {
        public const string Left = "left";
        public const string Right = "right";
    }

    /// <summary>
    /// 快捷入口：类型。
    /// </summary>
    internal static class ShortcutDockItemTypes
    {
        public const string File = "file";
        public const string Link = "link";
        public const string Program = "program";
    }

    /// <summary>
    /// 快捷入口：图标来源。
    /// </summary>
    internal static class ShortcutDockIconSources
    {
        public const string Default = "default";

        /// <summary>
        /// 用户自定义图标（选择一个图片/图标文件）。
        /// </summary>
        public const string Icon = "icon";

        /// <summary>
        /// 图标字体（内置图标字体中的某个字形）。
        /// </summary>
        public const string Font = "font";
    }

    /// <summary>
    /// 主（中间）Dock 左右两侧的快捷入口项。
    /// </summary>
    internal sealed class ShortcutDockItemSettings
    {
        /// <summary>
        /// 稳定标识符：用于列表编辑时定位项目（GUID 字符串）。
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// 放置位置：<see cref="ShortcutDockSides.Left"/> / <see cref="ShortcutDockSides.Right"/>。
        /// </summary>
        public string Side { get; set; } = ShortcutDockSides.Left;

        /// <summary>
        /// 类型：文件/链接/程序。
        /// </summary>
        public string Type { get; set; } = ShortcutDockItemTypes.File;

        /// <summary>
        /// 显示名称：为空时使用默认名称。
        /// </summary>
        public string? DisplayName { get; set; }

        /// <summary>
        /// 路径或网址：
        /// - 文件/程序：本地路径（例如 C:\a\b.txt 或 C:\a\app.exe）
        /// - 链接：http/https URL
        /// </summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>
        /// 启动参数：仅对 “程序” 类型生效。
        /// </summary>
        public string? Arguments { get; set; }

        /// <summary>
        /// 图标来源：默认/自定义图标。
        /// </summary>
        public string IconSource { get; set; } = ShortcutDockIconSources.Default;

        /// <summary>
        /// 自定义图标路径：仅当 <see cref="IconSource"/> 为 <see cref="ShortcutDockIconSources.Icon"/> 时使用。
        /// </summary>
        public string? IconPath { get; set; }

        /// <summary>
        /// 字体图标名称（Symbol）：仅当 <see cref="IconSource"/> 为 <see cref="ShortcutDockIconSources.Font"/> 时使用。
        /// </summary>
        public string? IconSymbol { get; set; }
    }

    /// <summary>
    /// Dock 相关设置（按钮顺序与显隐）。
    /// </summary>
    internal sealed class DockSettings
    {
        public List<string> LeftOrder { get; set; } = new(DockSettingsDefaults.LeftOrder);

        public List<string> ToolsOrder { get; set; } = new(DockSettingsDefaults.ToolsOrder);

        public List<string> UndoRedoOrder { get; set; } = new(DockSettingsDefaults.UndoRedoOrder);

        public List<string> PagesOrder { get; set; } = new(DockSettingsDefaults.PagesOrder);

        /// <summary>
        /// 撤销/重做区域是否可见。
        /// </summary>
        public bool IsUndoRedoVisible { get; set; } = true;

        /// <summary>
        /// 主 Dock 左右两侧的“快捷入口 Dock”是否可见。
        /// </summary>
        public bool IsShortcutDocksVisible { get; set; }

        /// <summary>
        /// 快捷入口列表（最多 5 个）。
        /// </summary>
        public List<ShortcutDockItemSettings> ShortcutItems { get; set; } = new();
    }
}

