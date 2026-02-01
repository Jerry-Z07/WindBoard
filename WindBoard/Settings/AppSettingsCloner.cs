using System;
using System.Collections.Generic;

namespace WindBoard.Settings
{
    /// <summary>
    /// 设置对象的深拷贝辅助。
    ///
    /// 说明：
    /// - 主要用于“保存快照”：避免并发保存时引用被外部修改。
    /// - 该拷贝不负责做归一化（例如修正非法 HEX），归一化由 Store/Service 统一处理。
    /// </summary>
    internal static class AppSettingsCloner
    {
        internal static AppSettings Clone(AppSettings settings)
        {
            if (settings is null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            return new AppSettings
            {
                Appearance = new AppearanceSettings
                {
                    CanvasBackgroundHex = settings.Appearance?.CanvasBackgroundHex ?? ColorHex.DefaultCanvasBackgroundHex,
                },
                Dock = new DockSettings
                {
                    LeftOrder = new List<string>(settings.Dock?.LeftOrder ?? DockSettingsDefaults.LeftOrder),
                    ToolsOrder = new List<string>(settings.Dock?.ToolsOrder ?? DockSettingsDefaults.ToolsOrder),
                    UndoRedoOrder = new List<string>(settings.Dock?.UndoRedoOrder ?? DockSettingsDefaults.UndoRedoOrder),
                    PagesOrder = new List<string>(settings.Dock?.PagesOrder ?? DockSettingsDefaults.PagesOrder),
                    IsUndoRedoVisible = settings.Dock?.IsUndoRedoVisible ?? true,
                },
                Writing = new WritingSettings
                {
                    Pen = new PenSettings
                    {
                        PaletteHexes = new List<string?>(
                            settings.Writing?.Pen?.PaletteHexes ?? PenSettingsDefaults.DefaultPaletteHexes),
                        ThicknessPresets = new List<float>(
                            settings.Writing?.Pen?.ThicknessPresets ?? PenSettingsDefaults.DefaultThicknessPresets),
                        UseThicknessSlider = settings.Writing?.Pen?.UseThicknessSlider ?? false,
                    },
                },
            };
        }
    }
}
