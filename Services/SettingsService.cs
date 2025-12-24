using System;
using System.IO;
using System.Windows.Media;
using Newtonsoft.Json;

namespace WindBoard
{
    // 应用设置模型（后续可以扩展更多设置项）
    public class AppSettings
    {
        // 背景颜色（HEX 或 #AARRGGBB）
        public string BackgroundColorHex { get; set; } = "#2E2F33";
    }

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
    }
}
