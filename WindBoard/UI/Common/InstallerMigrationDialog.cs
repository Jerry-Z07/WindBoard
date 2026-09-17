using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WindBoard.Errors;
using WindBoard.Localization;
using WindBoard.Logging;
using WindBoard.Persistence;

namespace WindBoard.UI.Common
{
    /// <summary>
    /// 旧安装版数据迁移弹窗（仅 MSIX 形态首次运行时出现）：
    /// - 确认 → 复用既有导入链路导入旧版设置（整包替换 + 归一化 + 落盘），写入自身数据目录；
    /// - 取消 → 不导入，仅写迁移标记，不再重复询问；
    /// - 同屏提供「打开已安装的应用」入口，引导卸载旧版（旧版与新版本是两个独立安装）。
    ///
    /// 说明：不主动启动旧 Inno 卸载器（需提权、UAC 行为未验证，design.md §3 已否决）。
    /// </summary>
    internal static class InstallerMigrationDialog
    {
        /// <summary>Windows「已安装的应用」设置页。</summary>
        private const string InstalledAppsSettingsUri = "ms-settings:appsfeatures";

        private const string LogCategory = "Migration";

        /// <summary>
        /// 展示迁移确认弹窗并按用户选择执行（异常由调用方的安全执行封装兜底）。
        /// </summary>
        internal static async Task RunAsync(XamlRoot xamlRoot, InstallerMigrationEvaluation evaluation)
        {
            ArgumentNullException.ThrowIfNull(xamlRoot);
            ArgumentNullException.ThrowIfNull(evaluation);

            var dialog = new ContentDialog
            {
                Title = L10n.Get("Migration_Dialog_Title"),
                Content = BuildContent(evaluation),
                PrimaryButtonText = L10n.Get("Migration_Dialog_ImportButton"),
                CloseButtonText = L10n.Get("Migration_Dialog_SkipButton"),
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = xamlRoot,
            };

            ContentDialogResult result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                // 导入为一次性启动动作：失败已在服务内记录日志，这里不再打扰用户。
                _ = await InstallerMigrationService.Instance.TryImportAsync(evaluation.LegacySettingsFilePath);
                return;
            }

            InstallerMigrationService.Instance.MarkDeclined();
        }

        private static UIElement BuildContent(InstallerMigrationEvaluation evaluation)
        {
            var panel = new StackPanel { Spacing = 12 };

            panel.Children.Add(new TextBlock
            {
                Text = L10n.Format("Migration_Dialog_Body_Fmt", evaluation.LegacySettingsFilePath),
                TextWrapping = TextWrapping.Wrap,
            });

            if (!string.IsNullOrWhiteSpace(evaluation.LegacyInstallDir))
            {
                // 旧版安装目录仅用于展示（读 HKLM 标记，不作为形态判定依据）。
                panel.Children.Add(new TextBlock
                {
                    Text = L10n.Format("Migration_Dialog_LegacyInstallDir_Fmt", evaluation.LegacyInstallDir),
                    TextWrapping = TextWrapping.Wrap,
                });
            }

            panel.Children.Add(new TextBlock
            {
                Text = L10n.Get("Migration_Dialog_UninstallHint"),
                TextWrapping = TextWrapping.Wrap,
            });

            var openInstalledAppsButton = new HyperlinkButton
            {
                Content = L10n.Get("Migration_Dialog_OpenInstalledApps"),
                Padding = new Thickness(0),
            };

            // 点击后打开系统「已安装的应用」页，弹窗保持打开，用户可继续选择导入或暂不导入。
            openInstalledAppsButton.Click += OnOpenInstalledAppsClick;
            panel.Children.Add(openInstalledAppsButton);

            return panel;
        }

        private static void OnOpenInstalledAppsClick(object sender, RoutedEventArgs e)
        {
            AppErrorGuard.FireAndForget(LogCategory, OpenInstalledAppsAsync);
        }

        private static async Task OpenInstalledAppsAsync()
        {
            bool launched = await Windows.System.Launcher.LaunchUriAsync(new Uri(InstalledAppsSettingsUri));
            if (!launched)
            {
                AppLog.Warn(LogCategory, $"打开「已安装的应用」失败（LaunchUriAsync 返回 false）：uri='{InstalledAppsSettingsUri}'");
            }
        }
    }
}
