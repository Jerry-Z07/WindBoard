using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MaterialDesignThemes.Wpf;
using WindBoard.Services;

namespace WindBoard
{
    public partial class MainWindow
    {
        // --- 视频展台 ---
        private async void BtnVideoPresenter_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var path = SettingsService.Instance.GetVideoPresenterPath();
                var args = SettingsService.Instance.GetVideoPresenterArgs();

                if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
                {
                    await ShowVideoPresenterNotFoundDialog(null);
                    return;
                }

                var psi = new ProcessStartInfo
                {
                    FileName = path,
                    Arguments = string.IsNullOrWhiteSpace(args) ? string.Empty : args,
                    UseShellExecute = true,
                    WorkingDirectory = System.IO.Path.GetDirectoryName(path) ?? Environment.CurrentDirectory
                };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                await ShowVideoPresenterNotFoundDialog(ex.Message);
            }
        }

        private Style? TryFindStyle(string key)
        {
            try
            {
                return FindResource(key) as Style;
            }
            catch
            {
                return null;
            }
        }

        private TextBlock CreateTextBlock(string text, string? styleKey, Thickness margin, bool wrap = false)
        {
            var tb = new TextBlock
            {
                Text = text,
                Margin = margin,
                TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap
            };

            if (!string.IsNullOrEmpty(styleKey))
            {
                var style = TryFindStyle(styleKey);
                if (style != null)
                {
                    tb.Style = style;
                }
            }

            return tb;
        }

        private Button CreateButton(string content, string? styleKey, object command, object? commandParameter)
        {
            var btn = new Button
            {
                Content = content,
                Command = (ICommand)command,
                CommandParameter = commandParameter
            };

            if (!string.IsNullOrEmpty(styleKey))
            {
                var style = TryFindStyle(styleKey);
                if (style != null)
                {
                    btn.Style = style;
                }
            }

            return btn;
        }

        private async Task ShowVideoPresenterNotFoundDialog(string? error)
        {
            string msg = "未找到“视频展台”程序。请前往 基本设置-视频展台 进行设置。";
            if (!string.IsNullOrWhiteSpace(error))
            {
                msg += "\n\n错误详情: " + error;
            }

            var stackPanel = new StackPanel { Margin = new Thickness(24) };

            var title = CreateTextBlock("视频展台不可用", "MaterialDesignHeadline6TextBlock", new Thickness(0, 0, 0, 12));
            var body = CreateTextBlock(msg, "MaterialDesignBodyMediumTextBlock", new Thickness(0, 0, 0, 16), wrap: true);

            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };

            var cancelButton = CreateButton("取消", "MaterialDesignFlatButton", DialogHost.CloseDialogCommand, false);
            var settingsButton = CreateButton("前往设置", "MaterialDesignFlatButton", DialogHost.CloseDialogCommand, true);

            buttonPanel.Children.Add(cancelButton);
            buttonPanel.Children.Add(settingsButton);

            stackPanel.Children.Add(title);
            stackPanel.Children.Add(body);
            stackPanel.Children.Add(buttonPanel);

            var result = await DialogHost.Show(stackPanel, "MainDialogHost");

            if (result is bool go && go)
            {
                var settingsWindow = new SettingsWindow { Owner = this };
                settingsWindow.ShowDialog();
            }
        }
    }
}

