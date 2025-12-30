using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;

namespace WindBoard
{
    public partial class SettingsWindow
    {
        // --- 外观设置 ---
        private Color _currentColor;
        private PopupBox? _colorPopupBox;

        // --- 视频展台设置（SettingsWindow 层） ---
        private bool _videoPresenterEnabled;
        private string _videoPresenterPath = string.Empty;
        private string _videoPresenterArgs = string.Empty;

        // --- 伪装设置 ---
        private bool _camouflageEnabled;
        private string _camouflageTitle = string.Empty;
        private string _camouflageSourcePath = string.Empty;
        private ImageSource? _camouflageIconPreview;
        private string _camouflageSourceDisplayName = "未选择文件";

        // --- 书写设置 ---
        private bool _strokeThicknessConsistencyEnabled;
        private bool _simulatedPressureEnabled;

        // --- 平滑参数设置 ---
        private bool _customSmoothingEnabled;
        private double _smoothingPenStepMm;
        private double _smoothingPenEpsilonMm;
        private double _smoothingPenFcMin;
        private double _smoothingPenBeta;
        private double _smoothingPenDCutoff;
        private double _smoothingFingerStepMm;
        private double _smoothingFingerEpsilonMm;
        private double _smoothingFingerFcMin;
        private double _smoothingFingerBeta;
        private double _smoothingFingerDCutoff;

        // --- 触摸手势设置 ---
        private bool _zoomPanTwoFingerOnly;

        private const string DefaultVideoPresenterPath = @"C:\\Program Files (x86)\\Seewo\\EasiCamera\\sweclauncher\\sweclauncher.exe";
        private const string DefaultVideoPresenterArgs = "-from en5";

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
