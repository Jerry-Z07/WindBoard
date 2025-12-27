using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WindBoard.Services
{
    public sealed class CamouflageService
    {
        private static readonly Lazy<CamouflageService> _lazy = new(() => new CamouflageService());
        public static CamouflageService Instance => _lazy.Value;

        private readonly string _cacheDir;
        private readonly string _cacheFileName = "camouflage.ico";

        private CamouflageService()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _cacheDir = Path.Combine(appData, "WindBoard", "camouflage");
        }

        public bool TryBuildIconCache(string sourcePath, out string cachePath, out ImageSource? preview)
        {
            cachePath = string.Empty;
            preview = null;

            try
            {
                if (!File.Exists(sourcePath)) return false;

                Directory.CreateDirectory(_cacheDir);
                cachePath = Path.Combine(_cacheDir, _cacheFileName);

                var bmp = ExtractBitmapSource(sourcePath);
                if (bmp == null) return false;

                SaveBitmapSourceAsIco(bmp, cachePath);
                preview = bmp;
                return true;
            }
            catch
            {
                cachePath = string.Empty;
                preview = null;
                return false;
            }
        }

        public ImageSource? LoadIconFromFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
            try
            {
                var ext = Path.GetExtension(path)?.ToLowerInvariant();
                if (ext == ".exe")
                {
                    return ExtractIconBitmapSourceFromExe(path);
                }

                return BitmapFrame.Create(new Uri(path), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            }
            catch
            {
                return null;
            }
        }

        public CamouflageResult BuildResult(ImageSource? defaultIcon, string defaultTitle)
        {
            var settings = SettingsService.Instance.Settings;

            var result = new CamouflageResult
            {
                Title = defaultTitle,
                Icon = defaultIcon,
                Enabled = false,
                IconPath = null
            };

            if (settings.CamouflageEnabled)
            {
                var title = string.IsNullOrWhiteSpace(settings.CamouflageTitle) ? defaultTitle : settings.CamouflageTitle.Trim();
                ImageSource? icon = null;
                string? iconPath = settings.CamouflageIconCachePath;

                if (!string.IsNullOrWhiteSpace(iconPath) && File.Exists(iconPath))
                {
                    icon = LoadIconFromFile(iconPath);
                }
                else if (!string.IsNullOrWhiteSpace(settings.CamouflageSourcePath))
                {
                    if (TryBuildIconCache(settings.CamouflageSourcePath, out var cache, out var preview))
                    {
                        iconPath = cache;
                        icon = preview;
                        try { SettingsService.Instance.SetCamouflageIconCachePath(cache); } catch { }
                    }
                    else
                    {
                        // 未能生成图标缓存，保持默认图标但仍应用标题
                        iconPath = null;
                    }
                }

                result.Title = title;
                result.Icon = icon ?? defaultIcon;
                result.IconPath = (!string.IsNullOrWhiteSpace(iconPath) && File.Exists(iconPath)) ? iconPath : null;
                result.Enabled = true;
                return result;
            }

            return result;
        }

        public void UpdateDesktopShortcut(string title, string? iconPath, bool enabled)
        {
            try
            {
                var exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrWhiteSpace(exePath)) return;

                var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                var shortcutPath = Path.Combine(desktop, "WindBoard.lnk");

                var shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return;

                var shellObj = Activator.CreateInstance(shellType);
                if (shellObj == null) return;
                dynamic shell = shellObj;
                dynamic shortcut = shell.CreateShortcut(shortcutPath);
                if (shortcut == null) return;
                shortcut.TargetPath = exePath;
                shortcut.WorkingDirectory = Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory;
                shortcut.WindowStyle = 1;
                shortcut.Description = title;
                shortcut.Arguments = string.Empty;
                shortcut.IconLocation = (enabled && !string.IsNullOrWhiteSpace(iconPath) && File.Exists(iconPath))
                    ? iconPath
                    : exePath ?? string.Empty;
                shortcut.Save();
            }
            catch
            {
                // 忽略快捷方式更新失败
            }
        }

        private BitmapSource? ExtractBitmapSource(string sourcePath)
        {
            var ext = Path.GetExtension(sourcePath)?.ToLowerInvariant();
            if (ext == ".exe")
            {
                return ExtractIconBitmapSourceFromExe(sourcePath);
            }

            try
            {
                var frame = BitmapFrame.Create(new Uri(sourcePath), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                return ResizeAndFormat(frame);
            }
            catch
            {
                return null;
            }
        }

        private BitmapSource? ExtractIconBitmapSourceFromExe(string exePath)
        {
            try
            {
                int[] sizes = new[] { 256, 128, 64, 48, 32, 24, 16 };
                foreach (var size in sizes)
                {
                    var icons = new IntPtr[1];
                    var ids = new int[1];
                    uint extracted = PrivateExtractIcons(exePath, 0, size, size, icons, ids, 1, 0);
                    if (extracted > 0 && icons[0] != IntPtr.Zero)
                    {
                        try
                        {
                            var src = Imaging.CreateBitmapSourceFromHIcon(
                                icons[0],
                                Int32Rect.Empty,
                                BitmapSizeOptions.FromWidthAndHeight(size, size));
                            return ResizeAndFormat(src);
                        }
                        finally
                        {
                            DestroyIcon(icons[0]);
                        }
                    }
                }
            }
            catch
            {
                // ignore
            }

            try
            {
                using var fallback = Icon.ExtractAssociatedIcon(exePath);
                if (fallback == null) return null;
                using var bmp = fallback.ToBitmap();
                using var ms = new MemoryStream();
                bmp.Save(ms, ImageFormat.Png);
                ms.Position = 0;
                var src = BitmapFrame.Create(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                return ResizeAndFormat(src);
            }
            catch
            {
                return null;
            }
        }

        private BitmapSource ResizeAndFormat(BitmapSource source)
        {
            const int maxSize = 256;
            double scale = 1.0;
            if (source.PixelWidth > maxSize || source.PixelHeight > maxSize)
            {
                var scaleX = (double)maxSize / source.PixelWidth;
                var scaleY = (double)maxSize / source.PixelHeight;
                scale = Math.Min(scaleX, scaleY);
            }

            BitmapSource formatted = source;
            if (Math.Abs(scale - 1.0) > 0.0001)
            {
                var transform = new ScaleTransform(scale, scale);
                formatted = new TransformedBitmap(source, transform);
            }

            if (formatted.Format != PixelFormats.Pbgra32)
            {
                formatted = new FormatConvertedBitmap(formatted, PixelFormats.Pbgra32, null, 0);
            }

            formatted.Freeze();
            return formatted;
        }

        private void SaveBitmapSourceAsIco(BitmapSource source, string path)
        {
            // ICO 采用 PNG 数据以保留 32 位和透明度
            byte widthByte = source.PixelWidth >= 256 ? (byte)0 : (byte)source.PixelWidth;
            byte heightByte = source.PixelHeight >= 256 ? (byte)0 : (byte)source.PixelHeight;

            byte[] pngData;
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using (var ms = new MemoryStream())
            {
                encoder.Save(ms);
                pngData = ms.ToArray();
            }

            using var fs = File.Create(path);
            using var bw = new BinaryWriter(fs);
            bw.Write((ushort)0); // reserved
            bw.Write((ushort)1); // icon type
            bw.Write((ushort)1); // image count
            bw.Write(widthByte); // width
            bw.Write(heightByte); // height
            bw.Write((byte)0); // colors
            bw.Write((byte)0); // reserved
            bw.Write((ushort)1); // planes
            bw.Write((ushort)32); // bit count
            bw.Write((uint)pngData.Length); // size
            bw.Write((uint)(6 + 16)); // offset
            bw.Write(pngData);
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr handle);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint PrivateExtractIcons(string lpszFile, int nIconIndex, int cxIcon, int cyIcon, IntPtr[] phicon, int[] piconid, uint nIcons, uint flags);
    }

    public class CamouflageResult
    {
        public string Title { get; set; } = string.Empty;
        public ImageSource? Icon { get; set; }
        public string? IconPath { get; set; }
        public bool Enabled { get; set; }
    }
}
