using System;
using System.IO;
using System.Windows.Media;
using Newtonsoft.Json;
using WindBoard.Models;

namespace WindBoard.Services
{
    // 设置服务：负责加载 / 保存 JSON，并向 UI 广播变更
    public sealed class SettingsService
    {
        private static readonly Lazy<SettingsService> _lazy = new(() => new SettingsService());
        public static SettingsService Instance => _lazy.Value;

        private readonly string _settingsDir;
        private readonly string _settingsPath;

        public AppSettings Settings { get; private set; } = new AppSettings();

        // 设置变更事件（MainWindow订阅以应用到画布）
        public event EventHandler<AppSettings>? SettingsChanged;

        private SettingsService()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _settingsDir = Path.Combine(appData, "WindBoard");
            _settingsPath = Path.Combine(_settingsDir, "settings.json");
        }

        public void Load()
        {
            try
            {
                if (!File.Exists(_settingsPath))
                {
                    Directory.CreateDirectory(_settingsDir);
                    Save(); // 写入默认配置
                    return;
                }

                var json = File.ReadAllText(_settingsPath);
                var settings = JsonConvert.DeserializeObject<AppSettings>(json);
                if (settings != null)
                {
                    Settings = settings;
                }
            }
            catch
            {
                // 保留默认设置
            }
        }

        private void Save()
        {
            try
            {
                Directory.CreateDirectory(_settingsDir);
                var json = JsonConvert.SerializeObject(Settings, Formatting.Indented);
                File.WriteAllText(_settingsPath, json);
            }
            catch
            {
                // 忽略持久化错误
            }
        }

        public Color GetBackgroundColor()
        {
            try
            {
                return (Color)ColorConverter.ConvertFromString(Settings.BackgroundColorHex);
            }
            catch
            {
                return Colors.White;
            }
        }

        public void SetBackgroundColor(Color color)
        {
            Settings.BackgroundColorHex = color.ToString();
            Save();
            SettingsChanged?.Invoke(this, Settings);
        }

        // --- 视频展台相关设置 ---
        public bool GetVideoPresenterEnabled() => Settings.VideoPresenterEnabled;

        public void SetVideoPresenterEnabled(bool enabled)
        {
            Settings.VideoPresenterEnabled = enabled;
            Save();
            SettingsChanged?.Invoke(this, Settings);
        }

        public string GetVideoPresenterPath() => Settings.VideoPresenterPath;

        public void SetVideoPresenterPath(string path)
        {
            Settings.VideoPresenterPath = path ?? string.Empty;
            Save();
            SettingsChanged?.Invoke(this, Settings);
        }

        public string GetVideoPresenterArgs() => Settings.VideoPresenterArgs;

        public void SetVideoPresenterArgs(string args)
        {
            Settings.VideoPresenterArgs = args ?? string.Empty;
            Save();
            SettingsChanged?.Invoke(this, Settings);
        }

        public (string Path, string Args) GetVideoPresenterConfig()
        {
            return (Settings.VideoPresenterPath, Settings.VideoPresenterArgs);
        }

        // --- 伪装设置 ---
        public bool GetCamouflageEnabled() => Settings.CamouflageEnabled;

        public void SetCamouflageEnabled(bool enabled)
        {
            Settings.CamouflageEnabled = enabled;
            Save();
            SettingsChanged?.Invoke(this, Settings);
        }

        public string GetCamouflageTitle() => Settings.CamouflageTitle;

        public void SetCamouflageTitle(string title)
        {
            Settings.CamouflageTitle = title ?? string.Empty;
            Save();
            SettingsChanged?.Invoke(this, Settings);
        }

        public string GetCamouflageSourcePath() => Settings.CamouflageSourcePath;

        public void SetCamouflageSourcePath(string path)
        {
            Settings.CamouflageSourcePath = path ?? string.Empty;
            Save();
            SettingsChanged?.Invoke(this, Settings);
        }

        public string GetCamouflageIconCachePath() => Settings.CamouflageIconCachePath;

        public void SetCamouflageIconCachePath(string path)
        {
            Settings.CamouflageIconCachePath = path ?? string.Empty;
            Save();
            SettingsChanged?.Invoke(this, Settings);
        }

        // --- 书写相关设置 ---
        public bool GetStrokeThicknessConsistencyEnabled() => Settings.StrokeThicknessConsistencyEnabled;

        public void SetStrokeThicknessConsistencyEnabled(bool enabled)
        {
            Settings.StrokeThicknessConsistencyEnabled = enabled;
            Save();
            SettingsChanged?.Invoke(this, Settings);
        }

        public bool GetSimulatedPressureEnabled() => Settings.SimulatedPressureEnabled;

        public void SetSimulatedPressureEnabled(bool enabled)
        {
            Settings.SimulatedPressureEnabled = enabled;
            Save();
            SettingsChanged?.Invoke(this, Settings);
        }

        // --- 触摸手势相关设置 ---
        public bool GetZoomPanTwoFingerOnly() => Settings.ZoomPanTwoFingerOnly;

        public void SetZoomPanTwoFingerOnly(bool enabled)
        {
            Settings.ZoomPanTwoFingerOnly = enabled;
            Save();
            SettingsChanged?.Invoke(this, Settings);
        }

        // --- 平滑参数相关设置 ---
        public bool GetCustomSmoothingEnabled() => Settings.CustomSmoothingEnabled;

        public void SetCustomSmoothingEnabled(bool enabled)
        {
            Settings.CustomSmoothingEnabled = enabled;
            Save();
            SettingsChanged?.Invoke(this, Settings);
        }

        public bool GetSmoothingWarningDismissed() => Settings.SmoothingWarningDismissed;

        public void SetSmoothingWarningDismissed(bool dismissed)
        {
            Settings.SmoothingWarningDismissed = dismissed;
            Save();
        }

        // 笔参数
        public double GetSmoothingPenStepMm() => Settings.SmoothingPenStepMm;
        public void SetSmoothingPenStepMm(double value) { Settings.SmoothingPenStepMm = value; Save(); SettingsChanged?.Invoke(this, Settings); }

        public double GetSmoothingPenEpsilonMm() => Settings.SmoothingPenEpsilonMm;
        public void SetSmoothingPenEpsilonMm(double value) { Settings.SmoothingPenEpsilonMm = value; Save(); SettingsChanged?.Invoke(this, Settings); }

        public double GetSmoothingPenFcMin() => Settings.SmoothingPenFcMin;
        public void SetSmoothingPenFcMin(double value) { Settings.SmoothingPenFcMin = value; Save(); SettingsChanged?.Invoke(this, Settings); }

        public double GetSmoothingPenBeta() => Settings.SmoothingPenBeta;
        public void SetSmoothingPenBeta(double value) { Settings.SmoothingPenBeta = value; Save(); SettingsChanged?.Invoke(this, Settings); }

        public double GetSmoothingPenDCutoff() => Settings.SmoothingPenDCutoff;
        public void SetSmoothingPenDCutoff(double value) { Settings.SmoothingPenDCutoff = value; Save(); SettingsChanged?.Invoke(this, Settings); }

        // 手指参数
        public double GetSmoothingFingerStepMm() => Settings.SmoothingFingerStepMm;
        public void SetSmoothingFingerStepMm(double value) { Settings.SmoothingFingerStepMm = value; Save(); SettingsChanged?.Invoke(this, Settings); }

        public double GetSmoothingFingerEpsilonMm() => Settings.SmoothingFingerEpsilonMm;
        public void SetSmoothingFingerEpsilonMm(double value) { Settings.SmoothingFingerEpsilonMm = value; Save(); SettingsChanged?.Invoke(this, Settings); }

        public double GetSmoothingFingerFcMin() => Settings.SmoothingFingerFcMin;
        public void SetSmoothingFingerFcMin(double value) { Settings.SmoothingFingerFcMin = value; Save(); SettingsChanged?.Invoke(this, Settings); }

        public double GetSmoothingFingerBeta() => Settings.SmoothingFingerBeta;
        public void SetSmoothingFingerBeta(double value) { Settings.SmoothingFingerBeta = value; Save(); SettingsChanged?.Invoke(this, Settings); }

        public double GetSmoothingFingerDCutoff() => Settings.SmoothingFingerDCutoff;
        public void SetSmoothingFingerDCutoff(double value) { Settings.SmoothingFingerDCutoff = value; Save(); SettingsChanged?.Invoke(this, Settings); }

        // 重置平滑参数为默认值
        public void ResetSmoothingParameters()
        {
            Settings.SmoothingPenStepMm = 0.9;
            Settings.SmoothingPenEpsilonMm = 0.15;
            Settings.SmoothingPenFcMin = 2.8;
            Settings.SmoothingPenBeta = 0.055;
            Settings.SmoothingPenDCutoff = 1.2;

            Settings.SmoothingFingerStepMm = 1.1;
            Settings.SmoothingFingerEpsilonMm = 0.3;
            Settings.SmoothingFingerFcMin = 1.8;
            Settings.SmoothingFingerBeta = 0.035;
            Settings.SmoothingFingerDCutoff = 1.2;

            Save();
            SettingsChanged?.Invoke(this, Settings);
        }
    }
}
