using System;
using System.IO;
using System.Text.Json;

namespace WindBoard.Settings
{
    /// <summary>
    /// JSON 设置文件的读写与归一化。
    /// 
    /// 设计点：
    /// - 读取失败/JSON 损坏时回退默认值，避免影响启动
    /// - 保存使用临时文件替换，降低写入中断导致文件损坏的概率
    /// - 所有设置在加载/更新后都会做一次“归一化”，确保内存态与落盘态可用
    /// </summary>
    internal sealed class AppSettingsStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            WriteIndented = true,
        };

        internal string FilePath { get; }

        internal AppSettingsStore(string filePath)
        {
            FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        }

        internal static AppSettingsStore CreateDefault()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WindBoard");
            string path = Path.Combine(dir, "settings.json");
            return new AppSettingsStore(path);
        }

        internal AppSettings LoadOrDefault()
        {
            try
            {
                if (!File.Exists(FilePath))
                {
                    return NormalizeInPlace(new AppSettings());
                }

                string json = File.ReadAllText(FilePath);
                AppSettings? settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                return NormalizeInPlace(settings ?? new AppSettings());
            }
            catch
            {
                // 读取/解析失败时回退到默认值，避免启动崩溃。
                return NormalizeInPlace(new AppSettings());
            }
        }

        internal void Save(AppSettings settings)
        {
            if (settings is null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            AppSettings snapshot = CloneAndNormalize(settings);
            string json = JsonSerializer.Serialize(snapshot, JsonOptions);

            string? directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string tempPath = FilePath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, FilePath, overwrite: true);
        }

        /// <summary>
        /// 把设置对象归一化到“可用”的状态（补齐 null、修正非法值等）。
        /// </summary>
        internal static AppSettings NormalizeInPlace(AppSettings settings)
        {
            if (settings is null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            settings.Appearance ??= new AppearanceSettings();
            settings.Appearance.CanvasBackgroundHex = ColorHex.NormalizeToHexRgbOrDefault(
                settings.Appearance.CanvasBackgroundHex,
                ColorHex.DefaultCanvasBackgroundHex);
            return settings;
        }

        private static AppSettings CloneAndNormalize(AppSettings settings)
        {
            // 设置对象结构简单，这里手动深拷贝，避免并发保存时引用被外部修改。
            var clone = new AppSettings
            {
                Appearance = new AppearanceSettings
                {
                    CanvasBackgroundHex = settings.Appearance?.CanvasBackgroundHex ?? ColorHex.DefaultCanvasBackgroundHex,
                },
            };

            return NormalizeInPlace(clone);
        }
    }
}

