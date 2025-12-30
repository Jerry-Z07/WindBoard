namespace WindBoard.Models.Export
{
    /// <summary>
    /// WBI 压缩级别
    /// </summary>
    public enum WbiCompressionLevel
    {
        /// <summary>无压缩（速度最快）</summary>
        None,
        /// <summary>快速压缩</summary>
        Fast,
        /// <summary>标准压缩（推荐）</summary>
        Standard,
        /// <summary>最大压缩（最小文件）</summary>
        Maximum
    }

    /// <summary>
    /// WBI 导出选项
    /// </summary>
    public sealed class WbiExportOptions : ExportOptionsBase
    {
        /// <summary>压缩级别</summary>
        public WbiCompressionLevel CompressionLevel { get; set; } = WbiCompressionLevel.Standard;

        /// <summary>
        /// 是否包含图片附件原始文件
        /// 视频和链接仅保存路径/URL
        /// </summary>
        public bool IncludeImageAssets { get; set; } = true;

        /// <summary>图片附件压缩质量 (1-100)</summary>
        public int ImageQuality { get; set; } = 80;

        /// <summary>图片最大尺寸（超过则缩放）</summary>
        public int MaxImageDimension { get; set; } = 2048;
    }
}
