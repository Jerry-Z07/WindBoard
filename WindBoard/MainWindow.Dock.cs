using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.UI;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;
using WindBoard.Board.Editing;
using WindBoard.Interaction;
using WindBoard.ShortcutDock;
using WindBoard.Settings;

namespace WindBoard
{
    /// <summary>
    /// 主窗口：Dock 配置应用与背景色同步相关代码。
    /// </summary>
    public sealed partial class MainWindow
    {
        private static readonly HttpClient ShortcutDockHttpClient = new();
        private static readonly Regex ShortcutDockIconLinkRegex = new(
            "<link[^>]*rel\\s*=\\s*[\"']?[^\"'>]*icon[^\"'>]*[\"']?[^>]*>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex ShortcutDockHrefRegex = new(
            "href\\s*=\\s*[\"'](?<href>[^\"']+)[\"']",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static MainWindow()
        {
            // 提供 UA，避免部分站点拒绝无 UA 请求。
            ShortcutDockHttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("WindBoard/1.0");
        }

        private int _shortcutDockApplyVersion;

        private static void UpdateCanvasBackgroundBrush(Color color)
        {
            // 页面管理缩略图等 XAML 视觉元素使用该资源刷子作为背景色；
            // 这里同步更新颜色，保证与 DirectX 渲染清屏色一致。
            if (Application.Current is null)
            {
                return;
            }

            if (Application.Current.Resources.TryGetValue("CanvasBackgroundBrush", out object? brushObj)
                && brushObj is SolidColorBrush brush)
            {
                brush.Color = color;
            }
        }

        private void ApplyDockSettingsToUi()
        {
            DockSettings dock = AppSettingsService.Instance.GetDockSettingsSnapshot();

            ApplyDockOrder(
                LeftDockPanel,
                dock.LeftOrder,
                new Dictionary<string, UIElement>(StringComparer.Ordinal)
                {
                    [DockItemIds.More] = MoreButton,
                    [DockItemIds.Minimize] = MinimizeButton,
                    [DockItemIds.Import] = ImportButton,
                });

            ApplyDockOrder(
                ToolsDockPanel,
                dock.ToolsOrder,
                new Dictionary<string, UIElement>(StringComparer.Ordinal)
                {
                    [DockItemIds.ToolSelect] = SelectToolToggleButton,
                    [DockItemIds.ToolPen] = PenToolToggleButton,
                    [DockItemIds.ToolEraser] = EraserToggleButton,
                });

            ApplyDockOrder(
                UndoRedoDockPanel,
                dock.UndoRedoOrder,
                new Dictionary<string, UIElement>(StringComparer.Ordinal)
                {
                    [DockItemIds.Undo] = UndoButton,
                    [DockItemIds.Redo] = RedoButton,
                });

            ApplyDockOrder(
                PagesDockPanel,
                dock.PagesOrder,
                new Dictionary<string, UIElement>(StringComparer.Ordinal)
                {
                    [DockItemIds.PagePrev] = PagePrevButton,
                    [DockItemIds.PageIndicator] = PageIndicatorButton,
                    [DockItemIds.PageNext] = PageNextButton,
                    [DockItemIds.PageAdd] = AddButton,
                });

            Visibility undoRedoVisibility = dock.IsUndoRedoVisible ? Visibility.Visible : Visibility.Collapsed;
            UndoRedoSeparator.Visibility = undoRedoVisibility;
            UndoRedoDockPanel.Visibility = undoRedoVisibility;

            ApplyShortcutDocksToUi(dock);
        }

        private void ApplyShortcutDocksToUi(DockSettings dock)
        {
            // 快捷入口 Dock（主 Dock 左右两侧）：
            // - 这里采用“重建按钮”方式，避免维护复杂的增量更新逻辑；
            // - 图标加载为异步：先展示 fallback，再异步替换为文件/网站图标。
            _shortcutDockApplyVersion++;
            int applyVersion = _shortcutDockApplyVersion;

            LeftShortcutDockPanel.Children.Clear();
            RightShortcutDockPanel.Children.Clear();

            if (!dock.IsShortcutDocksVisible)
            {
                LeftShortcutDockContainer.Visibility = Visibility.Collapsed;
                RightShortcutDockContainer.Visibility = Visibility.Collapsed;
                return;
            }

            int leftCount = 0;
            int rightCount = 0;

            foreach (ShortcutDockItemSettings item in dock.ShortcutItems)
            {
                // 允许设置页存在“未填路径”的占位项：主界面不展示。
                if (string.IsNullOrWhiteSpace(item.Path))
                {
                    continue;
                }

                StackPanel targetPanel;
                if (string.Equals(item.Side, ShortcutDockSides.Right, StringComparison.Ordinal))
                {
                    targetPanel = RightShortcutDockPanel;
                    rightCount++;
                }
                else
                {
                    targetPanel = LeftShortcutDockPanel;
                    leftCount++;
                }

                // 防御：避免异常数据导致 UI 过长。
                if (leftCount > 5 && targetPanel == LeftShortcutDockPanel)
                {
                    continue;
                }

                if (rightCount > 5 && targetPanel == RightShortcutDockPanel)
                {
                    continue;
                }

                Button button = CreateShortcutDockButton(item, applyVersion);
                targetPanel.Children.Add(button);
            }

            LeftShortcutDockContainer.Visibility = LeftShortcutDockPanel.Children.Count > 0
                ? Visibility.Visible
                : Visibility.Collapsed;
            RightShortcutDockContainer.Visibility = RightShortcutDockPanel.Children.Count > 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private Button CreateShortcutDockButton(ShortcutDockItemSettings item, int applyVersion)
        {
            Symbol fallbackSymbol = GetShortcutFallbackSymbol(item);
            string title = GetShortcutTitle(item);

            var iconImage = new Image
            {
                Width = 20,
                Height = 20,
                Stretch = Stretch.Uniform,
            };

            var fallbackIcon = new SymbolIcon
            {
                Symbol = fallbackSymbol,
            };

            var fontIcon = new SymbolIcon
            {
                Visibility = Visibility.Collapsed,
            };

            bool useFontIcon = false;
            if (string.Equals(item.IconSource, ShortcutDockIconSources.Font, StringComparison.Ordinal)
                && TryGetFontSymbol(item.IconSymbol, out Symbol symbol))
            {
                fontIcon.Symbol = symbol;
                fontIcon.Visibility = Visibility.Visible;
                fallbackIcon.Visibility = Visibility.Collapsed;
                useFontIcon = true;
            }

            var iconGrid = new Grid
            {
                Width = 20,
                Height = 20,
            };
            iconGrid.Children.Add(fallbackIcon);
            iconGrid.Children.Add(fontIcon);
            iconGrid.Children.Add(iconImage);

            var contentPanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
            };
            contentPanel.Children.Add(iconGrid);
            contentPanel.Children.Add(new TextBlock
            {
                FontSize = 11,
                Margin = new Thickness(0, 3, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
                MaxWidth = 56,
                Text = title,
            });

            var button = new Button
            {
                MinWidth = 60,
                MinHeight = 52,
                Padding = new Thickness(8, 6, 8, 6),
                Style = (Style)Application.Current.Resources["DockButtonStyle"],
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Content = contentPanel,
                Tag = item,
            };
            button.Click += OnShortcutDockItemClicked;

            // 异步加载图标：成功后会覆盖 fallback 图标。
            if (!useFontIcon)
            {
                _ = TryLoadShortcutIconIntoImageAsync(item, iconImage, fallbackIcon, applyVersion);
            }
            return button;
        }

        private static Symbol GetShortcutFallbackSymbol(ShortcutDockItemSettings item)
        {
            if (string.Equals(item.Type, ShortcutDockItemTypes.Link, StringComparison.Ordinal))
            {
                return Symbol.Link;
            }

            if (string.Equals(item.Type, ShortcutDockItemTypes.Program, StringComparison.Ordinal))
            {
                return Symbol.AllApps;
            }

            return Symbol.OpenFile;
        }

        private static string GetShortcutTitle(ShortcutDockItemSettings item)
        {
            if (!string.IsNullOrWhiteSpace(item.DisplayName))
            {
                return item.DisplayName.Trim();
            }

            if (string.Equals(item.Type, ShortcutDockItemTypes.Link, StringComparison.Ordinal))
            {
                string linkPath = item.Path?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(linkPath))
                {
                    return "未配置";
                }

                if (Uri.TryCreate(linkPath, UriKind.Absolute, out Uri? uri) && !string.IsNullOrWhiteSpace(uri.Host))
                {
                    // 展示 Host：避免过长 URL 挤压 Dock。
                    return uri.Host;
                }

                return "链接";
            }

            string path = item.Path?.Trim() ?? string.Empty;
            if (string.Equals(item.Type, ShortcutDockItemTypes.Program, StringComparison.Ordinal))
            {
                ShortcutDockLaunchHelper.NormalizeProgramLaunch(item.Path, item.Arguments, out string programTarget, out _);
                if (!string.IsNullOrWhiteSpace(programTarget))
                {
                    path = programTarget;
                }
            }

            if (string.IsNullOrWhiteSpace(path))
            {
                return "未配置";
            }

            try
            {
                string name = Path.GetFileNameWithoutExtension(path);
                return string.IsNullOrWhiteSpace(name) ? "文件" : name;
            }
            catch
            {
                return "文件";
            }
        }

        private static bool TryGetFontSymbol(string? symbolName, out Symbol symbol)
        {
            symbol = default;
            if (string.IsNullOrWhiteSpace(symbolName))
            {
                return false;
            }

            if (!Enum.TryParse(symbolName.Trim(), out Symbol parsed))
            {
                return false;
            }

            if (!Enum.IsDefined(typeof(Symbol), parsed))
            {
                return false;
            }

            symbol = parsed;
            return true;
        }

        private async Task TryLoadShortcutIconIntoImageAsync(
            ShortcutDockItemSettings item,
            Image target,
            UIElement fallbackIcon,
            int applyVersion)
        {
            try
            {
                ImageSource? source = await TryLoadShortcutIconAsync(item).ConfigureAwait(true);

                // 如果期间 UI 已刷新，丢弃过期结果，避免把旧图标写到新按钮上。
                if (applyVersion != _shortcutDockApplyVersion)
                {
                    return;
                }

                if (source is not null)
                {
                    target.Source = source;
                    // 成功加载图标后隐藏默认图标，避免叠在一起。
                    fallbackIcon.Visibility = Visibility.Collapsed;
                }
            }
            catch
            {
                // 图标加载失败：保持 fallback，不影响主流程。
            }
        }

        private async Task<ImageSource?> TryLoadShortcutIconAsync(ShortcutDockItemSettings item)
        {
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

            Uri? htmlIcon = await TryFindFaviconFromHtmlAsync(uri).ConfigureAwait(false);
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
                    byte[] bytes = await ShortcutDockHttpClient.GetByteArrayAsync(candidate).ConfigureAwait(false);
                    if (bytes is null || bytes.Length == 0)
                    {
                        continue;
                    }

                    // 简单防御：避免误下载到过大的文件。
                    if (bytes.Length > 256 * 1024)
                    {
                        continue;
                    }

                    using var stream = new InMemoryRandomAccessStream();
                    await stream.WriteAsync(bytes.AsBuffer());
                    stream.Seek(0);

                    var bitmap = new BitmapImage();
                    await bitmap.SetSourceAsync(stream);
                    return bitmap;
                }
                catch
                {
                    // 继续尝试下一个候选图标。
                }
            }

            return null;
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

        private async void OnShortcutDockItemClicked(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not ShortcutDockItemSettings item)
            {
                return;
            }

            Debug.WriteLine($"[ShortcutDock] 点击：type={item.Type}, path='{item.Path}', args='{item.Arguments}'");

            string target = ShortcutDockLaunchHelper.NormalizeInput(item.Path);
            if (string.IsNullOrWhiteSpace(target))
            {
                Debug.WriteLine("[ShortcutDock] 点击忽略：路径为空");
                return;
            }

            try
            {
                if (string.Equals(item.Type, ShortcutDockItemTypes.Link, StringComparison.Ordinal))
                {
                    if (!ShortcutDockLaunchHelper.TryNormalizeLinkUri(target, out Uri? uri))
                    {
                        Debug.WriteLine($"[ShortcutDock] 链接解析失败：input='{target}'");
                        await ShowShortcutDockErrorDialogAsync("链接无效", "请输入有效的网址（例如 https://example.com）。");
                        return;
                    }

                    Uri safeUri = uri!;
                    Debug.WriteLine($"[ShortcutDock] 打开链接：{safeUri}");
                    Process.Start(new ProcessStartInfo(safeUri.ToString()) { UseShellExecute = true });

                    return;
                }

                if (string.Equals(item.Type, ShortcutDockItemTypes.Program, StringComparison.Ordinal))
                {
                    ShortcutDockLaunchHelper.NormalizeProgramLaunch(item.Path, item.Arguments, out string programTarget, out string programArgs);
                    if (string.IsNullOrWhiteSpace(programTarget))
                    {
                        Debug.WriteLine("[ShortcutDock] 程序启动忽略：规范化后路径为空");
                        return;
                    }

                    bool fileExists = File.Exists(programTarget);
                    Debug.WriteLine($"[ShortcutDock] 程序启动：target='{programTarget}', args='{programArgs}', fileExists={fileExists}");
                    try
                    {
                        if (fileExists)
                        {
                            ProcessStartInfo info = ShortcutDockLaunchHelper.CreateProgramProcessStartInfo(programTarget, programArgs);
                            Debug.WriteLine($"[ShortcutDock] CreateProcess：useShell={info.UseShellExecute}, wd='{info.WorkingDirectory}', args='{info.Arguments}'");
                            Process.Start(info);
                        }
                        else
                        {
                            // 允许“应用别名 / App Paths / shell:AppsFolder”等非文件路径：交给 Shell 解析。
                            var shellInfo = new ProcessStartInfo(programTarget)
                            {
                                UseShellExecute = true,
                                Arguments = programArgs,
                            };
                            Debug.WriteLine($"[ShortcutDock] ShellExecute：args='{shellInfo.Arguments}'");
                            Process.Start(shellInfo);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[ShortcutDock] 程序启动异常：{ex}");
                        if (fileExists)
                        {
                            // 兜底：某些程序（例如需要提权的 exe）在 UseShellExecute=false 时可能启动失败，
                            // 这里回退到 ShellExecute 尝试触发系统默认行为（可能会弹出 UAC）。
                            try
                            {
                                var fallbackInfo = new ProcessStartInfo(programTarget)
                                {
                                    UseShellExecute = true,
                                    Arguments = programArgs,
                                    WorkingDirectory = Path.GetDirectoryName(programTarget) ?? string.Empty,
                                };
                                Debug.WriteLine($"[ShortcutDock] 程序启动兜底：useShell={fallbackInfo.UseShellExecute}, wd='{fallbackInfo.WorkingDirectory}', args='{fallbackInfo.Arguments}'");
                                Process.Start(fallbackInfo);
                            }
                            catch
                            {
                                await ShowShortcutDockErrorDialogAsync("启动失败", ex.Message);
                            }
                        }
                        else
                        {
                            await ShowShortcutDockErrorDialogAsync("程序不存在", "请检查程序路径或可执行命令是否正确。");
                        }
                    }
                    return;
                }

                // 默认按“文件”处理：交给系统默认程序打开。
                if (!File.Exists(target) && !Directory.Exists(target))
                {
                    Debug.WriteLine($"[ShortcutDock] 文件/文件夹不存在：'{target}'");
                    await ShowShortcutDockErrorDialogAsync("路径不存在", "请检查路径是否存在。");
                    return;
                }

                Debug.WriteLine($"[ShortcutDock] 打开文件/文件夹：'{target}'");
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ShortcutDock] 打开失败：{ex}");
                await ShowShortcutDockErrorDialogAsync("打开失败", ex.Message);
            }
        }

        private async Task ShowShortcutDockErrorDialogAsync(string title, string message)
        {
            XamlRoot? xamlRoot = TryGetDialogXamlRoot();
            if (xamlRoot is null)
            {
                return;
            }

            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = "关闭",
                XamlRoot = xamlRoot,
            };

            await dialog.ShowAsync();
        }

        private static void ApplyDockOrder(
            StackPanel panel,
            IReadOnlyList<string> order,
            IReadOnlyDictionary<string, UIElement> elementsById)
        {
            // 说明：Dock 的元素在 XAML 中是命名控件，这里仅调整它们在面板中的顺序，不创建/销毁控件。
            // 归一化已保证 order 只包含合法项并补齐缺失项，这里按 order 进行重排即可。
            panel.Children.Clear();

            foreach (string id in order)
            {
                if (elementsById.TryGetValue(id, out UIElement? element))
                {
                    panel.Children.Add(element);
                }
            }
        }

    }
}
