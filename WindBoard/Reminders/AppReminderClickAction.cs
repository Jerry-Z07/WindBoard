namespace WindBoard.Reminders
{
    /// <summary>
    /// 提醒点击动作：
    /// - Windows Toast：点击通知触发
    /// - 应用内弹条：点击消息触发
    /// </summary>
    internal enum AppReminderClickAction
    {
        None,

        /// <summary>
        /// 打开当前会话正在使用的“应用数据根目录”（安装版为 LocalAppData，便携版为 {AppBase}\data）。
        /// </summary>
        OpenAppDataRootDirectory,

        /// <summary>
        /// 打开当前会话正在使用的“日志目录”（通常为 {DataRoot}\Logs）。
        /// </summary>
        OpenLogsDirectory,
    }
}
