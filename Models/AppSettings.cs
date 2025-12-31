namespace WindBoard.Models
{
    // 应用设置模型（后续可以扩展更多设置项）
    public class AppSettings
    {
        // 背景颜色（HEX 或 #AARRGGBB）
        public string BackgroundColorHex { get; set; } = "#2E2F33";

        // 是否显示“视频展台”按钮
        public bool VideoPresenterEnabled { get; set; } = true;

        // 视频展台程序路径
        public string VideoPresenterPath { get; set; } = @"C:\\Program Files (x86)\\Seewo\\EasiCamera\\sweclauncher\\sweclauncher.exe";

        // 启动附加参数
        public string VideoPresenterArgs { get; set; } = "-from en5";

        // 伪装：是否启用
        public bool CamouflageEnabled { get; set; } = false;

        // 伪装：自定义标题
        public string CamouflageTitle { get; set; } = string.Empty;

        // 伪装：图标来源路径（exe/ico/png/jpg）
        public string CamouflageSourcePath { get; set; } = string.Empty;

        // 伪装：缓存生成的 ico 路径（供窗口/快捷方式复用）
        public string CamouflageIconCachePath { get; set; } = string.Empty;

        // 新笔迹粗细模式：开启后，不同缩放下书写的笔迹在同一缩放下粗细一致
        public bool StrokeThicknessConsistencyEnabled { get; set; } = false;

        // 模拟压感（签字笔风格）：用于无压感设备的轻微笔锋效果
        public bool SimulatedPressureEnabled { get; set; } = false;

        // 触摸缩放/平移：仅双指手势（开启后，三指及以上不参与缩放/平移）
        public bool ZoomPanTwoFingerOnly { get; set; } = false;
    }
}
