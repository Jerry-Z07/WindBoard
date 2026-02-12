using System;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;
using WindBoard.Board.Elements;
using WindBoard.Logging;
using WindBoard.Localization;
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
                AppLog.Error("Open", "外部打开异常", ex);
                await ShowOpenFailedDialogAsync(L10n.Get("Common_OpenFailed_Title"), ex.Message);
            }
        }

        private async Task OpenLinkAsync(BoardLinkElement link)
        {
            string raw = (link.Url ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(raw))
            {
                await ShowOpenFailedDialogAsync(L10n.Get("Open_CannotOpenLink_Title"), L10n.Get("Open_LinkEmpty_Message"));
                return;
            }

            if (!TryCreateUri(raw, out Uri? uri))
            {
                await ShowOpenFailedDialogAsync(L10n.Get("Open_CannotOpenLink_Title"), L10n.Format("Open_InvalidLinkFormat_Fmt", raw));
                return;
            }

            bool launched = await Launcher.LaunchUriAsync(uri);
            if (!launched)
            {
                await ShowOpenFailedDialogAsync(L10n.Get("Open_CannotOpenLink_Title"), L10n.Get("Open_LaunchLinkFailed_Message"));
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
                await ShowOpenFailedDialogAsync(L10n.Get("Open_CannotOpenFile_Title"), L10n.Get("Open_FilePathEmpty_Message"));
                return;
            }

            try
            {
                StorageFile file = await StorageFile.GetFileFromPathAsync(p);
                bool launched = await Launcher.LaunchFileAsync(file);
                if (!launched)
                {
                    await ShowOpenFailedDialogAsync(L10n.Get("Open_CannotOpenFile_Title"), L10n.Format("Open_LaunchFileFailed_Fmt", displayName));
                }
            }
            catch (FileNotFoundException)
            {
                await ShowOpenFailedDialogAsync(L10n.Get("Open_FileNotFound_Title"), L10n.Format("Open_FileNotFound_Fmt", displayName));
            }
            catch (UnauthorizedAccessException)
            {
                await ShowOpenFailedDialogAsync(L10n.Get("Open_CannotOpenFile_Title"), L10n.Format("Open_Unauthorized_Fmt", displayName));
            }
        }

        private async Task ShowOpenFailedDialogAsync(string title, string message)
        {
            try
            {
                if (_panel.XamlRoot is null)
                {
                    AppLog.Warn("Open", $"无法显示对话框（XamlRoot 为空）：{title} - {message}");
                    return;
                }

                var dialog = new ContentDialog
                {
                    Title = title,
                    Content = message,
                    CloseButtonText = L10n.Get("Common_Close"),
                    XamlRoot = _panel.XamlRoot,
                };

                await dialog.ShowAsync();
            }
            catch (Exception ex)
            {
                AppLog.Error("Open", "显示失败提示对话框异常", ex);
            }
        }
    }
}
