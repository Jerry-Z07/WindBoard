using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Threading;
using MaterialDesignThemes.Wpf;
using WindBoard.Core.Ink;

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
        private double _simulatedPressureStartTaperMm;
        private double _simulatedPressureEndTaperMm;
        private double _simulatedPressureSpeedMinMmPerSec;
        private double _simulatedPressureSpeedMaxMmPerSec;
        private double _simulatedPressureFastSpeedMinFactor;
        private double _simulatedPressureFloor;
        private double _simulatedPressureEndFloor;
        private double _simulatedPressureSmoothingTauMs;
        private DispatcherTimer? _simulatedPressurePersistTimer;
        private bool _isApplyingSimulatedPressureFields;

        private const string DefaultVideoPresenterPath = @"C:\\Program Files (x86)\\Seewo\\EasiCamera\\sweclauncher\\sweclauncher.exe";
        private const string DefaultVideoPresenterArgs = "-from en5";

        private static readonly SimulatedPressureConfig DefaultSimulatedPressureConfig = SimulatedPressureConfig.CreateDefault();

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

