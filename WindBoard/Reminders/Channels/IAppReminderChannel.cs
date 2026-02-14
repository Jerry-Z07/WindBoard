using System;
using Microsoft.UI.Xaml;

namespace WindBoard.Reminders.Channels
{
    /// <summary>
    /// 统一提醒通道接口：可以是 Windows 通知，也可以是应用内弹条等。
    /// </summary>
    internal interface IAppReminderChannel
    {
        /// <summary>
        /// 尝试展示提醒。
        /// </summary>
        /// <returns>展示成功返回 true，否则返回 false 并输出错误。</returns>
        bool TryShow(Window window, AppReminderMessage message, out Exception? error);
    }
}

