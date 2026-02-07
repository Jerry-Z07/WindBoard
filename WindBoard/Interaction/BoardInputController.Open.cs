using System;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;
using WindBoard.Board.Elements;
using Windows.Storage;
using Windows.System;

namespace WindBoard.Interaction
{
    /// <summary>
    /// 输入控制器：元素“外部打开”（双击）相关逻辑。
    /// </summary>
    internal sealed partial class BoardInputController
    {
        private const int ElementDoubleClickTimeoutMs = 500;
        private const float ElementDoubleClickMaxDistanceDip = 10.0f;

        private DateTimeOffset _lastElementClickAt;
        private Guid? _lastElementClickId;
        private Vector2 _lastElementClickScreenDip = Vector2.Zero;

        private void HandleElementClickForMaybeOpen(BoardElement element, Vector2 screenDip)
        {
            if (Tool != BoardTool.Select)
            {
                return;
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;

            bool isDoubleClick = _lastElementClickId == element.Id
                && (now - _lastElementClickAt).TotalMilliseconds <= ElementDoubleClickTimeoutMs
                && Vector2.DistanceSquared(screenDip, _lastElementClickScreenDip) <= ElementDoubleClickMaxDistanceDip * ElementDoubleClickMaxDistanceDip;

            _lastElementClickAt = now;
            _lastElementClickId = element.Id;
            _lastElementClickScreenDip = screenDip;

            if (!isDoubleClick)
            {
                return;
            }

            // 避免连续多次点击反复触发。
            _lastElementClickId = null;
            _lastElementClickAt = default;

            _ = OpenElementExternalAsync(element);
        }

        private async Task OpenElementExternalAsync(BoardElement element)
        {
            try
            {
                switch (element)
                {
                    case BoardLinkElement link:
                        await OpenLinkAsync(link);
                        return;

                    case BoardMediaElement media:
                        await OpenFilePathAsync(media.SourcePath, media.DisplayName);
                        return;

                    case BoardFileElement file:
                        await OpenFilePathAsync(file.SourcePath, file.DisplayName);
                        return;

                    default:
                        return;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Open] 外部打开异常：{ex}");
                await ShowOpenFailedDialogAsync("打开失败", ex.Message);
            }
        }

        private async Task OpenLinkAsync(BoardLinkElement link)
        {
            string raw = (link.Url ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(raw))
            {
                await ShowOpenFailedDialogAsync("无法打开链接", "链接为空。");
                return;
            }

            if (!TryCreateUri(raw, out Uri? uri))
            {
                await ShowOpenFailedDialogAsync("无法打开链接", $"链接格式无效：{raw}");
                return;
            }

            bool launched = await Launcher.LaunchUriAsync(uri);
            if (!launched)
            {
                await ShowOpenFailedDialogAsync("无法打开链接", "系统未能打开该链接。");
            }
        }

        private static bool TryCreateUri(string raw, out Uri? uri)
        {
            uri = null;

            if (Uri.TryCreate(raw, UriKind.Absolute, out Uri? absolute))
            {
                uri = absolute;
                return true;
            }

            // 用户可能省略 scheme（例如输入 example.com），这里兜底补 https://
            if (!raw.Contains("://", StringComparison.OrdinalIgnoreCase)
                && Uri.TryCreate("https://" + raw, UriKind.Absolute, out Uri? normalized))
            {
                uri = normalized;
                return true;
            }

            return false;
        }

        private async Task OpenFilePathAsync(string path, string displayName)
        {
            string p = (path ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(p))
            {
                await ShowOpenFailedDialogAsync("无法打开文件", "文件路径为空。");
                return;
            }

            try
            {
                StorageFile file = await StorageFile.GetFileFromPathAsync(p);
                bool launched = await Launcher.LaunchFileAsync(file);
                if (!launched)
                {
                    await ShowOpenFailedDialogAsync("无法打开文件", $"系统未能打开：{displayName}");
                }
            }
            catch (FileNotFoundException)
            {
                await ShowOpenFailedDialogAsync("文件不存在", $"找不到文件：{displayName}");
            }
            catch (UnauthorizedAccessException)
            {
                await ShowOpenFailedDialogAsync("无法打开文件", $"无权限访问：{displayName}");
            }
        }

        private async Task ShowOpenFailedDialogAsync(string title, string message)
        {
            try
            {
                if (_panel.XamlRoot is null)
                {
                    Debug.WriteLine($"[Open] 无法显示对话框（XamlRoot 为空）：{title} - {message}");
                    return;
                }

                var dialog = new ContentDialog
                {
                    Title = title,
                    Content = message,
                    CloseButtonText = "关闭",
                    XamlRoot = _panel.XamlRoot,
                };

                await dialog.ShowAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Open] 显示失败提示对话框异常：{ex}");
            }
        }
    }
}

