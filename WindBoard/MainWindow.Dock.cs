using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices.WindowsRuntime;
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

            var iconGrid = new Grid
            {
                Width = 20,
                Height = 20,
            };
            iconGrid.Children.Add(fallbackIcon);
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
            _ = TryLoadShortcutIconIntoImageAsync(item, iconImage, applyVersion);
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
            string path = item.Path?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(path))
            {
                return "未配置";
            }

            if (string.Equals(item.Type, ShortcutDockItemTypes.Link, StringComparison.Ordinal))
            {
                if (Uri.TryCreate(path, UriKind.Absolute, out Uri? uri) && !string.IsNullOrWhiteSpace(uri.Host))
                {
                    // 展示 Host：避免过长 URL 挤压 Dock。
                    return uri.Host;
                }

                return "链接";
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

        private async Task TryLoadShortcutIconIntoImageAsync(ShortcutDockItemSettings item, Image target, int applyVersion)
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

            return await TryLoadFileOrFolderIconAsync(item.Path).ConfigureAwait(true);
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

            Uri faviconUri = new(uri.GetLeftPart(UriPartial.Authority) + "/favicon.ico");

            try
            {
                // favicon 一般很小，这里直接读取 byte[]，失败则回退。
                byte[] bytes = await ShortcutDockHttpClient.GetByteArrayAsync(faviconUri).ConfigureAwait(false);
                if (bytes is null || bytes.Length == 0)
                {
                    return null;
                }

                // 简单防御：避免误下载到过大的文件。
                if (bytes.Length > 256 * 1024)
                {
                    return null;
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
                return null;
            }
        }

        private async void OnShortcutDockItemClicked(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not ShortcutDockItemSettings item)
            {
                return;
            }

            string target = ShortcutDockLaunchHelper.NormalizeInput(item.Path);
            if (string.IsNullOrWhiteSpace(target))
            {
                return;
            }

            try
            {
                if (string.Equals(item.Type, ShortcutDockItemTypes.Link, StringComparison.Ordinal))
                {
                    if (!ShortcutDockLaunchHelper.TryNormalizeLinkUri(target, out Uri? uri))
                    {
                        await ShowShortcutDockErrorDialogAsync("链接无效", "请输入有效的网址（例如 https://example.com）。");
                        return;
                    }

                    Uri safeUri = uri!;
                    Process.Start(new ProcessStartInfo(safeUri.ToString()) { UseShellExecute = true });

                    return;
                }

                if (string.Equals(item.Type, ShortcutDockItemTypes.Program, StringComparison.Ordinal))
                {
                    if (!File.Exists(target))
                    {
                        await ShowShortcutDockErrorDialogAsync("程序不存在", "请检查程序路径是否存在。");
                        return;
                    }

                    try
                    {
                        ProcessStartInfo info = ShortcutDockLaunchHelper.CreateProgramProcessStartInfo(target, item.Arguments);
                        Process.Start(info);
                    }
                    catch (Exception ex)
                    {
                        // 兜底：某些程序（例如需要提权的 exe）在 UseShellExecute=false 时可能启动失败，
                        // 这里回退到 ShellExecute 尝试触发系统默认行为（可能会弹出 UAC）。
                        try
                        {
                            string args = ShortcutDockLaunchHelper.NormalizeArguments(item.Arguments);
                            var fallbackInfo = new ProcessStartInfo(target)
                            {
                                UseShellExecute = true,
                                Arguments = args,
                                WorkingDirectory = Path.GetDirectoryName(target) ?? string.Empty,
                            };
                            Process.Start(fallbackInfo);
                        }
                        catch
                        {
                            await ShowShortcutDockErrorDialogAsync("启动失败", ex.Message);
                        }
                    }
                    return;
                }

                // 默认按“文件”处理：交给系统默认程序打开。
                if (!File.Exists(target) && !Directory.Exists(target))
                {
                    await ShowShortcutDockErrorDialogAsync("路径不存在", "请检查路径是否存在。");
                    return;
                }

                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
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
