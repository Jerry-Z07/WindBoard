using System;
using Microsoft.UI.Xaml;

namespace WindBoard.Reminders.Channels
{
    /// <summary>
    /// 应用内右上角弹条提醒通道（Toast 失败兜底 / 未来全屏模式通道）。
    /// </summary>
    internal sealed class InAppBannerReminderChannel : IAppReminderChannel
    {
        public bool TryShow(Window window, AppReminderMessage message, out Exception? error)
        {
            error = null;

            try
            {
                if (window is not MainWindow mainWindow)
                {
                    error = new InvalidOperationException("当前窗口不是 MainWindow，无法展示应用内弹条");
                    return false;
                }

                mainWindow.ShowInAppBanner(message);
                return true;
            }
            catch (Exception ex)
            {
                error = ex;
                return false;
            }
        }
    }
}
