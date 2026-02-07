namespace WindBoard.Board.Elements
{
    /// <summary>
    /// 多媒体元素：
    /// - 图片：可在画布上直接绘制位图；
    /// - 音频/视频：当前以占位卡片显示（后续可扩展内置播放或缩略图）。
    /// </summary>
    internal sealed class BoardMediaElement : BoardElement
    {
        public BoardMediaKind Kind { get; set; }

        /// <summary>
        /// 源文件路径（用于显示文件名、以及后续“打开文件”等动作）。
        /// </summary>
        public string SourcePath { get; set; } = string.Empty;

        /// <summary>
        /// 展示名称（默认取文件名）。
        /// </summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>
        /// 图片像素宽度（仅图片有效）。
        /// </summary>
        public int PixelWidth { get; set; }

        /// <summary>
        /// 图片像素高度（仅图片有效）。
        /// </summary>
        public int PixelHeight { get; set; }

        /// <summary>
        /// 图片像素数据（BGRA8 + 预乘 Alpha）。
        /// 
        /// 说明：该数据用于 Direct2D 绘制；如果解码失败则允许为 null，并在渲染端降级为占位卡片。
        /// </summary>
        public byte[]? Bgra8PremulPixels { get; set; }
    }
}

