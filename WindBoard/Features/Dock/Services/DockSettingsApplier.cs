using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WindBoard.Features.Dock.Models;
using WindBoard.Localization;
using WindBoard.Logging;
using WindBoard.UI.Common;

namespace WindBoard.Features.Dock.Services
{
    /// <summary>
    /// Dock 设置应用器：负责把 Dock 设置应用到主窗口 UI（含快捷入口 Dock 的按钮重建与点击处理）。
    /// </summary>
    internal sealed class DockSettingsApplier
    {
        private int _shortcutDockApplyVersion;
        private Func<XamlRoot?>? _tryGetDialogXamlRoot;

        internal void ApplyToMainWindow(DockMainWindowHost host, DockSettings dock, Func<XamlRoot?> tryGetDialogXamlRoot)
        {
            if (host is null)
            {
                throw new ArgumentNullException(nameof(host));
            }

            if (dock is null)
            {
                throw new ArgumentNullException(nameof(dock));
            }

            _tryGetDialogXamlRoot = tryGetDialogXamlRoot ?? throw new ArgumentNullException(nameof(tryGetDialogXamlRoot));

            DockOrderApplier.Apply(host.LeftDockPanel, dock.LeftOrder, host.LeftDockElementsById);
            DockOrderApplier.Apply(host.ToolsDockPanel, dock.ToolsOrder, host.ToolsDockElementsById);
            DockOrderApplier.Apply(host.UndoRedoDockPanel, dock.UndoRedoOrder, host.UndoRedoDockElementsById);
            DockOrderApplier.Apply(host.PagesDockPanel, dock.PagesOrder, host.PagesDockElementsById);

            Visibility undoRedoVisibility = dock.IsUndoRedoVisible ? Visibility.Visible : Visibility.Collapsed;
            host.UndoRedoSeparator.Visibility = undoRedoVisibility;
            host.UndoRedoDockPanel.Visibility = undoRedoVisibility;

            ApplyShortcutDocksToUi(host, dock);
        }

        private void ApplyShortcutDocksToUi(DockMainWindowHost host, DockSettings dock)
        {
            // 快捷入口 Dock（主 Dock 左右两侧）：
            // - 这里采用“重建按钮”方式，避免维护复杂的增量更新逻辑；
            // - 图标加载为异步：先展示 fallback，再异步替换为文件/网站图标。
            _shortcutDockApplyVersion++;
            int applyVersion = _shortcutDockApplyVersion;

            host.LeftShortcutDockPanel.Children.Clear();
            host.RightShortcutDockPanel.Children.Clear();

            if (!dock.IsShortcutDocksVisible)
            {
                host.LeftShortcutDockContainer.Visibility = Visibility.Collapsed;
                host.RightShortcutDockContainer.Visibility = Visibility.Collapsed;
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
                    targetPanel = host.RightShortcutDockPanel;
                    rightCount++;
                }
                else
                {
                    targetPanel = host.LeftShortcutDockPanel;
                    leftCount++;
                }

                // 防御：避免异常数据导致 UI 过长。
                if (leftCount > 5 && targetPanel == host.LeftShortcutDockPanel)
                {
                    continue;
                }

                if (rightCount > 5 && targetPanel == host.RightShortcutDockPanel)
                {
                    continue;
                }

                Button button = CreateShortcutDockButton(item, applyVersion);
                targetPanel.Children.Add(button);
            }

            host.LeftShortcutDockContainer.Visibility = host.LeftShortcutDockPanel.Children.Count > 0
                ? Visibility.Visible
                : Visibility.Collapsed;
            host.RightShortcutDockContainer.Visibility = host.RightShortcutDockPanel.Children.Count > 0
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
                    return L10n.Get("Common_NotConfigured");
                }

                if (Uri.TryCreate(linkPath, UriKind.Absolute, out Uri? uri) && !string.IsNullOrWhiteSpace(uri.Host))
                {
                    // 展示 Host：避免过长 URL 挤压 Dock。
                    return uri.Host;
                }

                return L10n.Get("Common_Link");
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
                return L10n.Get("Common_NotConfigured");
            }

            try
            {
                string name = Path.GetFileNameWithoutExtension(path);
                return string.IsNullOrWhiteSpace(name) ? L10n.Get("Common_File") : name;
            }
            catch
            {
                return L10n.Get("Common_File");
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
                ImageSource? source = await ShortcutDockIconLoader.TryLoadIconAsync(item).ConfigureAwait(true);

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
            catch (Exception ex)
            {
                // 图标加载失败：保持 fallback，不影响主流程。
                AppLog.Debug("ShortcutDock", $"图标加载失败：id={item.Id}, path='{item.Path}'", ex);
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
                AppLog.Debug("ShortcutDock", "点击忽略：路径为空");
                return;
            }

            try
            {
                if (string.Equals(item.Type, ShortcutDockItemTypes.Link, StringComparison.Ordinal))
                {
                    if (!ShortcutDockLaunchHelper.TryNormalizeLinkUri(target, out Uri? uri))
                    {
                        AppLog.Debug("ShortcutDock", $"链接解析失败：input='{target}'");
                        await ShowShortcutDockErrorDialogAsync(L10n.Get("ShortcutDock_InvalidLink_Title"), L10n.Get("ShortcutDock_InvalidLink_Message"));
                        return;
                    }

                    Uri safeUri = uri!;
                    Process.Start(new ProcessStartInfo(safeUri.ToString()) { UseShellExecute = true });

                    return;
                }

                if (string.Equals(item.Type, ShortcutDockItemTypes.Program, StringComparison.Ordinal))
                {
                    ShortcutDockLaunchHelper.NormalizeProgramLaunch(item.Path, item.Arguments, out string programTarget, out string programArgs);
                    if (string.IsNullOrWhiteSpace(programTarget))
                    {
                        AppLog.Debug("ShortcutDock", "程序启动忽略：规范化后路径为空");
                        return;
                    }

                    bool fileExists = File.Exists(programTarget);
                    try
                    {
                        if (fileExists)
                        {
                            ProcessStartInfo info = ShortcutDockLaunchHelper.CreateProgramProcessStartInfo(programTarget, programArgs);
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
                            Process.Start(shellInfo);
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLog.Error("ShortcutDock", "程序启动异常", ex);
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
                                AppLog.Warn("ShortcutDock", $"程序启动兜底：useShell={fallbackInfo.UseShellExecute}, wd='{fallbackInfo.WorkingDirectory}', args='{fallbackInfo.Arguments}'");
                                Process.Start(fallbackInfo);
                            }
                            catch (Exception fallbackEx)
                            {
                                AppLog.Error("ShortcutDock", "程序启动兜底失败", fallbackEx);
                                await ShowShortcutDockErrorDialogAsync(L10n.Get("ShortcutDock_LaunchFailed_Title"), ex.Message);
                            }
                        }
                        else
                        {
                            await ShowShortcutDockErrorDialogAsync(L10n.Get("ShortcutDock_ProgramNotFound_Title"), L10n.Get("ShortcutDock_ProgramNotFound_Message"));
                        }
                    }
                    return;
                }

                // 默认按“文件”处理：交给系统默认程序打开。
                if (!File.Exists(target) && !Directory.Exists(target))
                {
                    AppLog.Debug("ShortcutDock", $"文件/文件夹不存在：'{target}'");
                    await ShowShortcutDockErrorDialogAsync(L10n.Get("ShortcutDock_PathNotFound_Title"), L10n.Get("ShortcutDock_PathNotFound_Message"));
                    return;
                }

                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                AppLog.Error("ShortcutDock", "打开失败", ex);
                await ShowShortcutDockErrorDialogAsync(L10n.Get("Common_OpenFailed_Title"), ex.Message);
            }
        }

        private async Task ShowShortcutDockErrorDialogAsync(string title, string message)
        {
            XamlRoot? xamlRoot = TryGetDialogXamlRootSafe();
            if (xamlRoot is null)
            {
                return;
            }

            await DialogHelpers.ShowMessageAsync(xamlRoot, title, message);
        }

        private XamlRoot? TryGetDialogXamlRootSafe()
        {
            Func<XamlRoot?>? getter = _tryGetDialogXamlRoot;
            if (getter is null)
            {
                return null;
            }

            try
            {
                return getter();
            }
            catch (Exception ex)
            {
                // 兜底：弹窗属于辅助提示，获取失败时不应影响主流程。
                AppLog.Warn("ShortcutDock", "获取 XamlRoot 失败", ex);
                return null;
            }
        }
    }
}

