namespace WindBoard.Features.Camouflage.Models
{
    /// <summary>
    /// 伪装结果：由 CamouflageService 根据设置快照构建，供主窗口/设置页应用。
    /// </summary>
    internal sealed class CamouflageResult
    {
        public string Title { get; set; } = string.Empty;

        public bool Enabled { get; set; }

        /// <summary>
        /// 仅在开启伪装且图标有效时返回 .ico 路径；否则为 null（表示使用默认图标）。
        /// </summary>
        public string? IconPath { get; set; }
    }
}
