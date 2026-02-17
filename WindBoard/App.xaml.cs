using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;
using WindBoard.Logging;
using WindBoard.Localization;
using WindBoard.Settings;
using Microsoft.Windows.AppNotifications;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace WindBoard
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        private Window? _window;

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            // 日志尽可能早初始化：这样启动阶段（资源加载/设置加载）的问题也能落盘，方便用户排查。
            AppLog.Initialize();

            // 捕获系统语言：用于“跟随系统”模式下的回退与运行中切换。
            AppLanguageService.CaptureSystemCulturesIfNeeded();

            InitializeComponent();

            // 全局异常捕获：避免“静默失败”，并把关键堆栈落盘到日志文件。
            UnhandledException += OnAppUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            AppSettingsService.Instance.Load();

            // 应用语言偏好：必须在创建任何 Window/加载任何 XAML 前执行，否则 LocExtension 的取值可能会缓存旧语言。
            try
            {
                AppLanguagePreference preference = AppSettingsService.Instance.GetLanguagePreference();
                AppLanguageService.Apply(preference);
            }
            catch (Exception ex)
            {
                // 语言应用失败不应阻断启动：记录日志并继续。
                AppLog.Warn("L10n", "应用语言偏好失败，将继续使用系统语言", ex);
            }

            L10n.Initialize();

            // 统一提醒系统：注册 Windows Toast 通知通道。
            // 说明：在某些环境（未注册/系统限制）下可能失败，这里不阻断启动，后续会自动降级为应用内弹条。
            try
            {
                AppNotificationManager.Default.Register();
                AppLog.Info("Reminders", "Windows 通知通道注册成功");
            }
            catch (Exception ex)
            {
                AppLog.Warn("Reminders", "Windows 通知通道注册失败，将自动降级为应用内弹条", ex);
            }

            // 应用设置加载完成后，按 settings.json 的配置重新应用日志级别/保留天数等。
            LoggingSettingsSnapshot logging = AppSettingsService.Instance.GetLoggingSettingsSnapshot();
            AppLogOptions defaults = AppLogOptions.CreateDefault();
            AppLog.Initialize(
                new AppLogOptions
                {
                    MinimumLevel = logging.MinimumLevel,
                    FileEnabled = logging.FileEnabled,
                    DebugOutputEnabled = defaults.DebugOutputEnabled,
                    LogDirectory = defaults.LogDirectory,
                    RetentionDays = logging.RetentionDays,
                },
                writeInitLog: false);

            string version = AppInfo.Version;
            AppLog.Info("App", $"应用启动：version={version}, args='{args.Arguments ?? string.Empty}', logFile='{AppLog.CurrentLogFilePath ?? "(null)"}'");

            _window = new MainWindow();
            _window.Activate();
        }

        private static void OnAppUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            try
            {
                AppLog.Critical("App", "WinUI UnhandledException", e.Exception);
            }
            catch
            {
                // 兜底：全局异常处理本身不能再抛异常
            }
        }

        private static void OnAppDomainUnhandledException(object sender, System.UnhandledExceptionEventArgs e)
        {
            try
            {
                if (e.ExceptionObject is Exception ex)
                {
                    AppLog.Critical("App", $"AppDomain UnhandledException：isTerminating={e.IsTerminating}", ex);
                }
                else
                {
                    AppLog.Critical("App", $"AppDomain UnhandledException：isTerminating={e.IsTerminating}, exceptionObjectType={e.ExceptionObject?.GetType().FullName ?? "(null)"}");
                }
            }
            catch
            {
                // 兜底：全局异常处理本身不能再抛异常
            }
        }

        private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            try
            {
                AppLog.Error("App", "TaskScheduler.UnobservedTaskException", e.Exception);

                // 标记已观察：避免宿主将其升级为进程级异常（不同运行时/配置下行为可能不同）。
                e.SetObserved();
            }
            catch
            {
                // 兜底：全局异常处理本身不能再抛异常
            }
        }
    }
}
