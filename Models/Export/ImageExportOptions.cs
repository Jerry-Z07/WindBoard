using System.Windows.Media;

namespace WindBoard.Models.Export
{
    /// <summary>
    /// 图片导出分辨率预设
    /// </summary>
    public enum ImageResolutionPreset
    {
        /// <summary>1080p (1920×1080)</summary>
        Preset1080p,
        /// <summary>2K (2560×1440)</summary>
        Preset2K,
        /// <summary>4K (3840×2160)</summary>
        Preset4K,
        /// <summary>自定义</summary>
        Custom
    }

    /// <summary>
    /// 图片导出选项
    /// </summary>
    public sealed class ImageExportOptions : ExportOptionsBase
    {
        /// <summary>图片格式</summary>
        public ExportFormat Format { get; set; } = ExportFormat.Png;

        /// <summary>分辨率预设</summary>
        public ImageResolutionPreset ResolutionPreset { get; set; } = ImageResolutionPreset.Preset1080p;

        /// <summary>目标宽度（像素）</summary>
        public int Width { get; set; } = 1920;

        /// <summary>目标高度（像素）</summary>
        public int Height { get; set; } = 1080;

        /// <summary>是否保持笔迹宽高比</summary>
        public bool KeepAspectRatio { get; set; } = true;

        /// <summary>背景颜色（与画布背景一致）</summary>
        public Color BackgroundColor { get; set; } = Color.FromRgb(0x0F, 0x12, 0x16);

        /// <summary>JPEG 质量 (1-100)</summary>
        public int JpegQuality { get; set; } = 90;

        /// <summary>应用分辨率预设</summary>
        public void ApplyPreset(ImageResolutionPreset preset)
        {
            ResolutionPreset = preset;
            switch (preset)
            {
                case ImageResolutionPreset.Preset1080p:
                    Width = 1920;
                    Height = 1080;
                    break;
                case ImageResolutionPreset.Preset2K:
                    Width = 2560;
                    Height = 1440;
                    break;
                case ImageResolutionPreset.Preset4K:
                    Width = 3840;
                    Height = 2160;
                    break;
            }
        }
    }
}
