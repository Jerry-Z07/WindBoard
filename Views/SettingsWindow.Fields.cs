using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;
using WindBoard.Models;

namespace WindBoard
{
    public partial class SettingsWindow
    {
        // --- 基本设置 ---
        private AppLanguage _appLanguage = AppLanguage.Chinese;

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
        private string _camouflageSourceDisplayName = string.Empty;

        // --- 书写设置 ---
        private StrokeSmoothingMode _strokeSmoothingMode = StrokeSmoothingMode.RawInput;
        private bool _strokeThicknessConsistencyEnabled;
        private bool _simulatedPressureEnabled;

        // --- 触摸手势设置 ---
        private bool _zoomPanTwoFingerOnly;

        // --- 更新设置 ---
        private bool _autoCheckUpdatesEnabled;

        private const string DefaultVideoPresenterPath = @"C:\\Program Files (x86)\\Seewo\\EasiCamera\\sweclauncher\\sweclauncher.exe";
        private const string DefaultVideoPresenterArgs = "-from en5";

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
