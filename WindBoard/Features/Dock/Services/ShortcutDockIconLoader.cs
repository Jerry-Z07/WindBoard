using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;
using WindBoard.Features.Dock.Models;

namespace WindBoard.Features.Dock.Services
{
    /// <summary>
    /// 快捷入口图标加载器：
    /// - 支持自定义图标（本地文件）
    /// - 支持链接 favicon（从 HTML 或常见路径探测）
    /// - 支持本地文件/文件夹缩略图
    /// 
    /// 说明：图标加载属于“可选增强”，失败时返回 null，不应阻断 UI。
    /// </summary>
    internal static class ShortcutDockIconLoader
    {
        private static readonly HttpClient ShortcutDockHttpClient = new();
        private static readonly Regex ShortcutDockIconLinkRegex = new(
            "<link[^>]*rel\\s*=\\s*[\"']?[^\"'>]*icon[^\"'>]*[\"']?[^>]*>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex ShortcutDockHrefRegex = new(
            "href\\s*=\\s*[\"'](?<href>[^\"']+)[\"']",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static ShortcutDockIconLoader()
        {
            // 提供 UA，避免部分站点拒绝无 UA 请求。
            ShortcutDockHttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("WindBoard/1.0");
        }

        public static async Task<ImageSource?> TryLoadIconAsync(ShortcutDockItemSettings item)
        {
            if (item is null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            // 自定义图标优先：允许用户覆盖默认逻辑。
            if (string.Equals(item.IconSource, ShortcutDockIconSources.Icon, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(item.IconPath))
            {
                ImageSource? custom = await TryLoadBitmapFromFilePathAsync(item.IconPath).ConfigureAwait(true);
                if (custom is not null)
                {
                    return custom;
                }
            }

            string type = item.Type ?? string.Empty;
            if (string.Equals(type, ShortcutDockItemTypes.Link, StringComparison.Ordinal))
            {
                return await TryLoadFaviconAsync(item.Path).ConfigureAwait(true);
            }

            string iconTarget = item.Path;
            if (string.Equals(type, ShortcutDockItemTypes.Program, StringComparison.Ordinal))
            {
                ShortcutDockLaunchHelper.NormalizeProgramLaunch(item.Path, item.Arguments, out string programTarget, out _);
                if (!string.IsNullOrWhiteSpace(programTarget))
                {
                    iconTarget = programTarget;
                }
            }

            return await TryLoadFileOrFolderIconAsync(iconTarget).ConfigureAwait(true);
        }

        private static async Task<ImageSource?> TryLoadBitmapFromFilePathAsync(string? filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return null;
            }

            try
            {
                StorageFile file = await StorageFile.GetFileFromPathAsync(filePath).AsTask().ConfigureAwait(true);
                using IRandomAccessStream stream = await file.OpenReadAsync().AsTask().ConfigureAwait(true);
                var bitmap = new BitmapImage();
                await bitmap.SetSourceAsync(stream);
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        private static async Task<ImageSource?> TryLoadFileOrFolderIconAsync(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            try
            {
                const uint desiredSize = 48;

                if (File.Exists(path))
                {
                    StorageFile file = await StorageFile.GetFileFromPathAsync(path).AsTask().ConfigureAwait(true);
                    using StorageItemThumbnail thumb = await file.GetThumbnailAsync(
                        ThumbnailMode.ListView,
                        desiredSize,
                        ThumbnailOptions.UseCurrentScale).AsTask().ConfigureAwait(true);

                    var bitmap = new BitmapImage();
                    await bitmap.SetSourceAsync(thumb);
                    return bitmap;
                }

                if (Directory.Exists(path))
                {
                    StorageFolder folder = await StorageFolder.GetFolderFromPathAsync(path).AsTask().ConfigureAwait(true);
                    using StorageItemThumbnail thumb = await folder.GetThumbnailAsync(
                        ThumbnailMode.ListView,
                        desiredSize,
                        ThumbnailOptions.UseCurrentScale).AsTask().ConfigureAwait(true);

                    var bitmap = new BitmapImage();
                    await bitmap.SetSourceAsync(thumb);
                    return bitmap;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private static async Task<ImageSource?> TryLoadFaviconAsync(string? urlOrHost)
        {
            if (string.IsNullOrWhiteSpace(urlOrHost))
            {
                return null;
            }

            // 支持输入 Host（example.com）或完整 URL（https://example.com/path）。
            string input = urlOrHost.Trim();
            if (!Uri.TryCreate(input, UriKind.Absolute, out Uri? uri))
            {
                // 兼容用户只填 host：默认补 https。
                if (!Uri.TryCreate("https://" + input, UriKind.Absolute, out uri))
                {
                    return null;
                }
            }

            if (string.IsNullOrWhiteSpace(uri.Host))
            {
                return null;
            }

            Uri baseUri = new(uri.GetLeftPart(UriPartial.Authority));
            List<Uri> candidates = new();

            Uri? htmlIcon = await TryFindFaviconFromHtmlAsync(uri).ConfigureAwait(true);
            if (htmlIcon is not null)
            {
                candidates.Add(htmlIcon);
            }

            candidates.Add(new Uri(baseUri, "/favicon.ico"));
            candidates.Add(new Uri(baseUri, "/favicon.png"));
            candidates.Add(new Uri(baseUri, "/apple-touch-icon.png"));
            candidates.Add(new Uri(baseUri, "/apple-touch-icon-precomposed.png"));

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Uri candidate in candidates)
            {
                if (!seen.Add(candidate.AbsoluteUri))
                {
                    continue;
                }

                try
                {
                    // favicon 一般很小，这里直接读取 byte[]，失败则回退。
                    byte[] bytes = await ShortcutDockHttpClient.GetByteArrayAsync(candidate).ConfigureAwait(true);
                    if (bytes is null || bytes.Length == 0)
                    {
                        continue;
                    }

                    // 简单防御：避免误下载到过大的文件。
                    if (bytes.Length > 256 * 1024)
                    {
                        continue;
                    }

                    if (!IsLikelyImageBytes(bytes))
                    {
                        continue;
                    }

                    ImageSource? source = await TryDecodeImageSourceAsync(bytes).ConfigureAwait(true);
                    if (source is not null)
                    {
                        return source;
                    }
                }
                catch
                {
                    // 继续尝试下一个候选图标。
                }
            }

            return null;
        }

        private static bool IsLikelyImageBytes(byte[] bytes)
        {
            if (bytes.Length < 4)
            {
                return false;
            }

            // PNG: 89 50 4E 47
            if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
            {
                return true;
            }

            // JPEG: FF D8
            if (bytes[0] == 0xFF && bytes[1] == 0xD8)
            {
                return true;
            }

            // GIF: 47 49 46
            if (bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46)
            {
                return true;
            }

            // BMP: 42 4D
            if (bytes[0] == 0x42 && bytes[1] == 0x4D)
            {
                return true;
            }

            // ICO/CUR: 00 00 01 00 或 00 00 02 00
            if (bytes[0] == 0x00 && bytes[1] == 0x00 && (bytes[2] == 0x01 || bytes[2] == 0x02) && bytes[3] == 0x00)
            {
                return true;
            }

            return false;
        }

        private static async Task<ImageSource?> TryDecodeImageSourceAsync(byte[] bytes)
        {
            try
            {
                using var stream = new InMemoryRandomAccessStream();
                await stream.WriteAsync(bytes.AsBuffer());
                stream.Seek(0);

                BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);
                SoftwareBitmap bitmap = await decoder.GetSoftwareBitmapAsync();
                if (bitmap.BitmapPixelFormat != BitmapPixelFormat.Bgra8
                    || bitmap.BitmapAlphaMode != BitmapAlphaMode.Premultiplied)
                {
                    SoftwareBitmap converted = SoftwareBitmap.Convert(
                        bitmap,
                        BitmapPixelFormat.Bgra8,
                        BitmapAlphaMode.Premultiplied);
                    bitmap.Dispose();
                    bitmap = converted;
                }

                var source = new SoftwareBitmapSource();
                await source.SetBitmapAsync(bitmap);
                bitmap.Dispose();
                return source;
            }
            catch
            {
                return null;
            }
        }

        private static async Task<Uri?> TryFindFaviconFromHtmlAsync(Uri pageUri)
        {
            string? html = await TryDownloadHtmlAsync(pageUri).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(html))
            {
                return null;
            }

            foreach (Match match in ShortcutDockIconLinkRegex.Matches(html))
            {
                Match hrefMatch = ShortcutDockHrefRegex.Match(match.Value);
                if (!hrefMatch.Success)
                {
                    continue;
                }

                string href = hrefMatch.Groups["href"].Value.Trim();
                if (string.IsNullOrWhiteSpace(href))
                {
                    continue;
                }

                if (href.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (Uri.TryCreate(pageUri, href, out Uri? iconUri))
                {
                    return iconUri;
                }
            }

            return null;
        }

        private static async Task<string?> TryDownloadHtmlAsync(Uri pageUri)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, pageUri);
                request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml");

                using HttpResponseMessage response = await ShortcutDockHttpClient
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead)
                    .ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                string? contentType = response.Content.Headers.ContentType?.MediaType;
                if (contentType is not null
                    && !contentType.Contains("html", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                using Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

                char[] buffer = new char[256 * 1024];
                int read = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                if (read <= 0)
                {
                    return null;
                }

                return new string(buffer, 0, read);
            }
            catch
            {
                return null;
            }
        }
    }
}
