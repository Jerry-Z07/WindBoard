namespace WindBoard.Settings
{
    /// <summary>
    /// 应用设置根对象（JSON 文件的顶层结构）。
    /// </summary>
    internal sealed class AppSettings
    {
        public AppearanceSettings Appearance { get; set; } = new();
    }

    internal sealed class AppearanceSettings
    {
        /// <summary>
        /// 画布背景色（#RRGGBB 或 #AARRGGBB）。
        /// </summary>
        public string CanvasBackgroundHex { get; set; } = ColorHex.DefaultCanvasBackgroundHex;
    }
}

