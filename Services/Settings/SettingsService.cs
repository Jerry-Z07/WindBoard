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

        // --- 语言设置 ---
        public AppLanguage GetLanguage() => Settings.Language;

        public void SetLanguage(AppLanguage language)
        {
            Settings.Language = language;
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

        public string GetCamouflageShortcutLastGeneratedSignature() => Settings.CamouflageShortcutLastGeneratedSignature;

        public void SetCamouflageShortcutLastGeneratedSignature(string signature)
        {
            Settings.CamouflageShortcutLastGeneratedSignature = signature ?? string.Empty;
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

        public StrokeSmoothingMode GetStrokeSmoothingMode() => Settings.StrokeSmoothingMode;

        public void SetStrokeSmoothingMode(StrokeSmoothingMode mode)
        {
            Settings.StrokeSmoothingMode = mode;
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
    }
}
