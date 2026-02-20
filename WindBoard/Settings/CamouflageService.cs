using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using WindBoard.Logging;
using WindBoard.Persistence;

namespace WindBoard.Settings
{
    /// <summary>
    /// 伪装服务：
    /// - 根据设置生成窗口标题与图标结果
    /// - 从 exe/图片生成 .ico 缓存
    /// - 生成/更新桌面快捷方式
    /// </summary>
    internal sealed class CamouflageService
    {
        internal static CamouflageService Instance { get; } = new();

        private const string CamouflageIconFileName = "camouflage.ico";
        private const string DefaultIconFileName = "default.ico";

        private readonly string _cacheDir;

        private CamouflageService()
        {
            // 统一由 AppDataPaths 决定缓存目录（安装版/便携版不同策略）。
            _cacheDir = AppDataPaths.CamouflageCacheDirectory;
            if (string.IsNullOrWhiteSpace(_cacheDir))
            {
                // 极端兜底：避免目录解析失败导致后续图标缓存/快捷方式逻辑异常。
                _cacheDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "WindBoard",
                    "camouflage");
            }
        }

        internal static string ComputeCamouflageShortcutSettingsSignature(bool enabled, string? title, string? sourcePath, string? iconCachePath)
        {
            // 说明：
            // - 签名用于判断“伪装配置是否变化”，以便在用户修改设置后自动刷新一次桌面快捷方式；
            // - 同时用于避免每次启动都生成快捷方式（减少对桌面的干扰）。
            string payload = string.Join(
                "\n",
                enabled ? "1" : "0",
                title ?? string.Empty,
                sourcePath ?? string.Empty,
                iconCachePath ?? string.Empty);

            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
            return Convert.ToHexString(hash);
        }

        internal string GetCamouflageShortcutSettingsSignature(CamouflageSettingsSnapshot snapshot)
        {
            if (snapshot is null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            return ComputeCamouflageShortcutSettingsSignature(
                snapshot.Enabled,
                snapshot.Title,
                snapshot.SourcePath,
                snapshot.IconCachePath);
        }

        internal CamouflageResult BuildResult(CamouflageSettingsSnapshot snapshot, string defaultTitle)
        {
            if (snapshot is null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            var result = new CamouflageResult
            {
                Title = defaultTitle,
                Enabled = false,
                IconPath = null,
            };

            if (!snapshot.Enabled)
            {
                return result;
            }

            string title = string.IsNullOrWhiteSpace(snapshot.Title) ? defaultTitle : snapshot.Title.Trim();
            string? iconPath = null;

            if (!string.IsNullOrWhiteSpace(snapshot.IconCachePath) && File.Exists(snapshot.IconCachePath))
            {
                iconPath = snapshot.IconCachePath;
            }
            else if (!string.IsNullOrWhiteSpace(snapshot.SourcePath) && File.Exists(snapshot.SourcePath))
            {
                // 缓存丢失/为空时，尝试从来源重新生成一次，增强鲁棒性。
                if (TryBuildCamouflageIconCache(snapshot.SourcePath, out string cachePath, out _, out _))
                {
                    iconPath = cachePath;
                    try
                    {
                        AppSettingsService.Instance.Update(s => s.General.Camouflage.IconCachePath = cachePath);
                    }
                    catch (Exception ex)
                    {
                        AppLog.Warn("Camouflage", "持久化图标缓存路径失败", ex);
                    }
                }
            }

            result.Title = title;
            result.Enabled = true;
            result.IconPath = (!string.IsNullOrWhiteSpace(iconPath) && File.Exists(iconPath)) ? iconPath : null;
            return result;
        }

        internal bool TryBuildCamouflageIconCache(string sourcePath, out string cachePath, out byte[]? previewBytes, out string? errorMessage)
        {
            return TryBuildIconCacheCore(sourcePath, CamouflageIconFileName, out cachePath, out previewBytes, out errorMessage);
        }

        internal bool TryEnsureDefaultIconCache(out string cachePath)
        {
            cachePath = string.Empty;
            try
            {
                Directory.CreateDirectory(_cacheDir);

                // 说明：默认图标缓存文件名携带版本号，避免“升级后仍沿用旧缓存”导致图标不更新。
                string version = global::WindBoard.AppInfo.Version;
                string fileName = string.IsNullOrWhiteSpace(version) || string.Equals(version, "unknown", StringComparison.OrdinalIgnoreCase)
                    ? DefaultIconFileName
                    : $"default_{SanitizeFileNameToken(version)}.ico";

                string path = Path.Combine(_cacheDir, fileName);

                if (File.Exists(path))
                {
                    cachePath = path;
                    return true;
                }

                // 优先从应用资源目录读取 icon.png 生成默认图标：这样即便 EXE 图标提取失败，窗口/任务栏图标也更稳定。
                string assetIconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "icon.png");
                if (File.Exists(assetIconPath))
                {
                    bool okFromAsset = TryBuildIconCacheCore(assetIconPath, fileName, out path, out _, out string? assetError);
                    if (okFromAsset)
                    {
                        cachePath = path;
                        return true;
                    }

                    if (!string.IsNullOrWhiteSpace(assetError))
                    {
                        AppLog.Warn("Camouflage", $"生成默认图标缓存失败，将回退到 EXE 图标：asset='{assetIconPath}', error='{assetError}'");
                    }
                }

                string? exePath = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
                {
                    return false;
                }

                bool ok = TryBuildIconCacheCore(exePath, fileName, out path, out _, out _);
                cachePath = ok ? path : string.Empty;
                return ok;
            }
            catch (Exception ex)
            {
                AppLog.Warn("Camouflage", "生成默认图标缓存失败", ex);
                cachePath = string.Empty;
                return false;
            }
        }

        private static string SanitizeFileNameToken(string token)
        {
            // 文件名中不允许出现特殊字符：把它们统一替换为 '_'，避免写入失败。
            char[] invalidChars = Path.GetInvalidFileNameChars();
            char[] buffer = token.ToCharArray();
            for (int i = 0; i < buffer.Length; i++)
            {
                char c = buffer[i];
                if (Array.IndexOf(invalidChars, c) >= 0)
                {
                    buffer[i] = '_';
                }
            }

            return new string(buffer);
        }

        internal bool TryUpdateDesktopShortcut(string title, string? iconPath, bool enabled, out string shortcutPath, out string? errorMessage)
        {
            return TryUpdateDesktopShortcut(
                title,
                iconPath,
                enabled,
                previousShortcutPath: null,
                out shortcutPath,
                out errorMessage);
        }

        internal bool TryUpdateDesktopShortcut(
            string title,
            string? iconPath,
            bool enabled,
            string? previousShortcutPath,
            out string shortcutPath,
            out string? errorMessage)
        {
            object? linkObj = null;

            shortcutPath = string.Empty;
            errorMessage = null;

            try
            {
                string? exePath = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(exePath))
                {
                    errorMessage = "无法获取程序路径。";
                    return false;
                }

                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

                shortcutPath = GetDesktopShortcutPath(desktop, title, exePath);

                // 使用 ShellLink COM 接口写入 .lnk，避免依赖 WScript.Shell（dynamic + trimmer 风险更高）。
                string workingDirectory = Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory;
                string iconLocation = (enabled && !string.IsNullOrWhiteSpace(iconPath) && File.Exists(iconPath))
                    ? iconPath
                    : exePath;

                linkObj = new ShellLink();
                var link = (IShellLinkW)linkObj;
                link.SetPath(exePath);
                link.SetWorkingDirectory(workingDirectory);
                link.SetDescription(title);
                link.SetArguments(string.Empty);
                link.SetShowCmd(1);
                link.SetIconLocation(iconLocation, 0);

                ((IPersistFile)linkObj).Save(shortcutPath, true);

                // 标题变化时，快捷方式文件名会变化：写入新文件后清理旧文件，避免桌面出现多个快捷方式。
                TryCleanupOldDesktopShortcut(previousShortcutPath, shortcutPath, exePath, desktop);
                return true;
            }
            catch (Exception ex)
            {
                AppLog.Error("Camouflage", "更新桌面快捷方式失败", ex);
                errorMessage = ex.Message;
                return false;
            }
            finally
            {
                ReleaseComObject(linkObj, "ShellLink");
            }
        }

        private static void TryCleanupOldDesktopShortcut(string? previousShortcutPath, string newShortcutPath, string exePath, string desktopDir)
        {
            try
            {
                string? previous = string.IsNullOrWhiteSpace(previousShortcutPath) ? null : previousShortcutPath.Trim();
                if (!string.IsNullOrWhiteSpace(previous)
                    && !string.Equals(previous, newShortcutPath, StringComparison.OrdinalIgnoreCase)
                    && File.Exists(previous)
                    && IsShortcutPointingToExe(previous, exePath))
                {
                    TryDeleteFile(previous);
                    return;
                }

                // 兼容旧逻辑：早期版本固定写 WindBoard.lnk，这里在“未记录旧路径”时尝试清理一次。
                string legacy = Path.Combine(desktopDir, "WindBoard.lnk");
                if (string.Equals(legacy, newShortcutPath, StringComparison.OrdinalIgnoreCase) || !File.Exists(legacy))
                {
                    return;
                }

                if (IsShortcutPointingToExe(legacy, exePath))
                {
                    TryDeleteFile(legacy);
                }
            }
            catch
            {
                // 清理失败不应影响主流程（例如文件被占用/权限问题）。
            }
        }

        private static string GetDesktopShortcutPath(string desktopDir, string title, string exePath)
        {
            // 说明：
            // - 快捷方式“外显命名”主要由文件名决定；这里用标题生成 .lnk 文件名。
            // - 为避免覆盖用户桌面上同名的其它快捷方式，若检测到同名 .lnk 且不指向当前程序，自动追加后缀。
            string baseName = SanitizeShortcutFileName(title);

            for (int attempt = 0; attempt < 20; attempt++)
            {
                string name = attempt switch
                {
                    0 => baseName,
                    1 => baseName + " (WindBoard)",
                    _ => $"{baseName} (WindBoard {attempt})",
                };

                name = SanitizeShortcutFileName(name);
                string path = Path.Combine(desktopDir, name + ".lnk");

                if (!File.Exists(path) || IsShortcutPointingToExe(path, exePath))
                {
                    return path;
                }
            }

            // 兜底：极端情况下仍冲突，回退到固定命名避免无限循环。
            return Path.Combine(desktopDir, "WindBoard.lnk");
        }

        private static bool IsShortcutPointingToExe(string shortcutPath, string exePath)
        {
            if (!TryReadShortcutTargetPath(shortcutPath, out string? target) || string.IsNullOrWhiteSpace(target))
            {
                return false;
            }

            try
            {
                return string.Equals(Path.GetFullPath(target), Path.GetFullPath(exePath), StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
                // ignore
            }
        }

        private static bool TryReadShortcutTargetPath(string shortcutPath, out string? targetPath)
        {
            targetPath = null;
            object? linkObj = null;

            try
            {
                linkObj = new ShellLink();
                ((IPersistFile)linkObj).Load(shortcutPath, 0);

                var link = (IShellLinkW)linkObj;
                var sb = new StringBuilder(260);
                var data = new WIN32_FIND_DATAW();
                link.GetPath(sb, sb.Capacity, ref data, 0);
                targetPath = sb.ToString();
                return !string.IsNullOrWhiteSpace(targetPath);
            }
            catch
            {
                targetPath = null;
                return false;
            }
            finally
            {
                ReleaseComObject(linkObj, "ShellLink(Read)");
            }
        }

        private static string SanitizeShortcutFileName(string? title)
        {
            string fallbackName = global::WindBoard.AppDisplayName.Get();
            string name = (title ?? string.Empty).Trim();

            // 去掉用户误输入的扩展名，避免出现 ".lnk.lnk"。
            if (name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                name = Path.GetFileNameWithoutExtension(name).Trim();
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                name = fallbackName;
            }

            char[] invalidChars = Path.GetInvalidFileNameChars();
            foreach (char c in invalidChars)
            {
                name = name.Replace(c, '_');
            }

            // Windows 文件名不能以空格/点结尾。
            name = name.TrimEnd(' ', '.');
            if (string.IsNullOrWhiteSpace(name))
            {
                name = fallbackName;
            }

            // 避免保留设备名导致创建失败。
            if (IsWindowsReservedDeviceName(name))
            {
                name = name + "_";
            }

            const int maxLength = 80;
            if (name.Length > maxLength)
            {
                name = name[..maxLength].TrimEnd(' ', '.');
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = fallbackName;
                }
            }

            return name;
        }

        private static bool IsWindowsReservedDeviceName(string name)
        {
            string upper = name.Trim().ToUpperInvariant();
            if (upper is "CON" or "PRN" or "AUX" or "NUL")
            {
                return true;
            }

            if (upper.Length == 4 && upper.StartsWith("COM", StringComparison.Ordinal))
            {
                char n = upper[3];
                return n >= '1' && n <= '9';
            }

            if (upper.Length == 4 && upper.StartsWith("LPT", StringComparison.Ordinal))
            {
                char n = upper[3];
                return n >= '1' && n <= '9';
            }

            return false;
        }

        private bool TryBuildIconCacheCore(string sourcePath, string cacheFileName, out string cachePath, out byte[]? previewBytes, out string? errorMessage)
        {
            cachePath = string.Empty;
            previewBytes = null;
            errorMessage = null;

            try
            {
                if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                {
                    errorMessage = "文件不存在。";
                    return false;
                }

                Directory.CreateDirectory(_cacheDir);
                cachePath = Path.Combine(_cacheDir, cacheFileName);

                string ext = (Path.GetExtension(sourcePath) ?? string.Empty).ToLowerInvariant();
                if (string.Equals(ext, ".ico", StringComparison.Ordinal))
                {
                    // 对 .ico：直接复制，保留多尺寸信息（同时避免 System.Drawing 对 PNG 压缩 ICO 的兼容性问题）。
                    File.Copy(sourcePath, cachePath, overwrite: true);
                    previewBytes = TryReadSmallFileBytes(cachePath, maxBytes: 512 * 1024);
                    return true;
                }

                using Bitmap? bitmap = string.Equals(ext, ".exe", StringComparison.Ordinal)
                    ? ExtractBitmapFromExe(sourcePath)
                    : LoadBitmapFromImageFile(sourcePath);

                if (bitmap is null)
                {
                    errorMessage = "无法读取图标/图片。";
                    cachePath = string.Empty;
                    return false;
                }

                using Bitmap formatted = ResizeAndFormat(bitmap);
                byte[] pngBytes = EncodeBitmapToPng(formatted);
                previewBytes = pngBytes;

                SavePngAsSingleImageIco(pngBytes, formatted.Width, formatted.Height, cachePath);
                return true;
            }
            catch (Exception ex)
            {
                AppLog.Warn("Camouflage", $"构建图标缓存失败：source='{sourcePath}'", ex);
                cachePath = string.Empty;
                previewBytes = null;
                errorMessage = ex.Message;
                return false;
            }
        }

        private static byte[]? TryReadSmallFileBytes(string path, int maxBytes)
        {
            try
            {
                var info = new FileInfo(path);
                if (info.Length <= 0 || info.Length > maxBytes)
                {
                    return null;
                }

                return File.ReadAllBytes(path);
            }
            catch
            {
                return null;
            }
        }

        private static Bitmap? LoadBitmapFromImageFile(string path)
        {
            try
            {
                using Image img = Image.FromFile(path);
                var bmp = new Bitmap(img.Width, img.Height, PixelFormat.Format32bppPArgb);
                using Graphics g = Graphics.FromImage(bmp);

                // 关键点：用 SourceCopy 避免 alpha 叠加导致边缘发灰。
                g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                g.DrawImage(img, new Rectangle(0, 0, bmp.Width, bmp.Height));
                return bmp;
            }
            catch
            {
                return null;
            }
        }

        private static Bitmap? ExtractBitmapFromExe(string exePath)
        {
            // 优先使用 PrivateExtractIcons 取到较大尺寸；失败再用 ExtractAssociatedIcon 兜底。
            try
            {
                int[] sizes = [256, 128, 64, 48, 32, 24, 16];
                foreach (int size in sizes)
                {
                    var icons = new IntPtr[1];
                    var ids = new int[1];
                    uint extracted = PrivateExtractIcons(exePath, 0, size, size, icons, ids, 1, 0);
                    if (extracted > 0 && icons[0] != IntPtr.Zero)
                    {
                        try
                        {
                            using Icon icon = Icon.FromHandle(icons[0]);
                            using Icon clone = (Icon)icon.Clone();
                            using Bitmap bmp = clone.ToBitmap();
                            return new Bitmap(bmp);
                        }
                        finally
                        {
                            DestroyIcon(icons[0]);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn("Camouflage", $"从 EXE 提取图标失败：'{exePath}'", ex);
            }

            try
            {
                using Icon? fallback = Icon.ExtractAssociatedIcon(exePath);
                if (fallback is null)
                {
                    return null;
                }

                using Bitmap bmp = fallback.ToBitmap();
                return new Bitmap(bmp);
            }
            catch
            {
                return null;
            }
        }

        private static Bitmap ResizeAndFormat(Bitmap source)
        {
            const int maxSize = 256;

            int width = source.Width;
            int height = source.Height;

            double scale = 1.0;
            if (width > maxSize || height > maxSize)
            {
                scale = Math.Min(maxSize / (double)width, maxSize / (double)height);
            }

            int targetWidth = Math.Max(1, (int)Math.Round(width * scale));
            int targetHeight = Math.Max(1, (int)Math.Round(height * scale));

            var bmp = new Bitmap(targetWidth, targetHeight, PixelFormat.Format32bppPArgb);
            using Graphics g = Graphics.FromImage(bmp);
            g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.DrawImage(source, new Rectangle(0, 0, targetWidth, targetHeight));
            return bmp;
        }

        private static byte[] EncodeBitmapToPng(Bitmap bitmap)
        {
            using var ms = new MemoryStream();
            bitmap.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }

        private static void SavePngAsSingleImageIco(byte[] pngData, int width, int height, string path)
        {
            // ICO 采用 PNG 数据以保留 32 位和透明度（单图标条目）。
            byte widthByte = width >= 256 ? (byte)0 : (byte)width;
            byte heightByte = height >= 256 ? (byte)0 : (byte)height;

            using FileStream fs = File.Create(path);
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
        private static extern uint PrivateExtractIcons(
            string lpszFile,
            int nIconIndex,
            int cxIcon,
            int cyIcon,
            IntPtr[] phicon,
            int[] piconid,
            uint nIcons,
            uint flags);

        // --- ShellLink COM：用于创建/更新 .lnk 文件 ---
        [ComImport]
        [Guid("000214F9-0000-0000-C000-000000000046")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, ref WIN32_FIND_DATAW pfd, uint fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(out short pwHotkey);
            void SetHotkey(short wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
            void Resolve(IntPtr hwnd, uint fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }

        [ComImport]
        [Guid("0000010B-0000-0000-C000-000000000046")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPersistFile
        {
            void GetClassID(out Guid pClassID);
            void IsDirty();
            void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
            void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, bool fRemember);
            void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
            void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
        }

        [ComImport]
        [Guid("00021401-0000-0000-C000-000000000046")]
        private sealed class ShellLink
        {
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WIN32_FIND_DATAW
        {
            public uint dwFileAttributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
            public uint nFileSizeHigh;
            public uint nFileSizeLow;
            public uint dwReserved0;
            public uint dwReserved1;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string cFileName;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
            public string cAlternateFileName;
        }

        private static void ReleaseComObject(object? comObj, string name)
        {
            if (comObj is null || !Marshal.IsComObject(comObj))
            {
                return;
            }

            try
            {
                Marshal.FinalReleaseComObject(comObj);
            }
            catch (Exception ex)
            {
                AppLog.Debug("Camouflage", $"释放 COM 对象失败：{name}, ex={ex}");
            }
        }
    }

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
