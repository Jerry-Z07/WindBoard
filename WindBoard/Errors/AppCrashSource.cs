namespace WindBoard.Errors
{
    /// <summary>
    /// 未处理异常来源（用于崩溃报告分类）。
    /// </summary>
    internal enum AppCrashSource
    {
        /// <summary>
        /// WinUI UI 线程未处理异常（Microsoft.UI.Xaml.Application.UnhandledException）。
        /// </summary>
        WinUIUnhandledException,

        /// <summary>
        /// AppDomain 未处理异常（System.AppDomain.UnhandledException）。
        /// </summary>
        AppDomainUnhandledException,
    }
}

