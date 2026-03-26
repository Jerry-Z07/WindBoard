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
using WindBoard.Errors;
using WindBoard.Fonts;
using WindBoard.Logging;
using WindBoard.Localization;
using WindBoard.Persistence;
using WindBoard.Reminders;
using WindBoard.Settings;
using WindBoard.Updates;
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
        private static int _notificationInvokedHooked;

        /// <summary>
        /// 获取主窗口引用（仅用于同程序集内部的调试/工具入口）。
        /// 说明：调试页需要通过 MainWindow 提供的应用内弹条能力做通道测试；
        /// 这里返回可空引用，调用方需自行处理窗口不存在的情况。
        /// </summary>
        internal MainWindow? TryGetMainWindow() => _window as MainWindow;

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

            // 全局异常捕获：避免“静默失败”，并把关键堆栈落盘到日志文件。
            // 说明：尽量早 Hook（在 InitializeComponent 前），避免 XAML 初始化阶段异常漏掉。
            UnhandledException += OnAppUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            // 图标字体（Segoe Fluent Icons）可用性处理：
            // - Win10 便携版：启动时私有加载内置 ttf
            // - Win10 安装版：依赖安装器安装字体（这里也会做兜底处理）
            // - Win11：系统自带
            // 说明：尽量在 InitializeComponent 前执行，避免 XAML/渲染层过早创建图标控件导致缓存错误字体。
            SegoeFluentIconsFontLoader.InitializeForCurrentProcess();

            InitializeComponent();
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            // 应用资源覆盖：统一让 SymbolIcon / SymbolIconSource 等走同一套图标字体。
            // 说明：部分环境下在 App 构造函数阶段读取 Application.Resources 可能触发 COMException；
            // 这里放到 OnLaunched（且在创建任何 Window 前）执行，兼容性更好。
            try
            {
                SegoeFluentIconsFontLoader.ApplyToResources(Resources);
            }
            catch (Exception ex)
            {
                AppLog.Warn("Fonts", "获取 Application.Resources 失败，将跳过图标字体资源覆盖", ex);
            }

            AppSettingsService.Instance.Load();

            // 应用语言偏好：必须在创建任何 Window/加载任何 XAML 前执行，否则 LocExtension 的取值可能会缓存旧语言。
            try
            {
                string preference = AppSettingsService.Instance.GetLanguagePreference();
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
                EnsureAppNotificationInvokedHandlerHooked();
                AppNotificationManager.Default.Register();
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

            if (AppDataPaths.InstallKind == AppInstallKind.Portable && !AppDataPaths.UsingPortableDataDirectory)
            {
                string error = AppDataPaths.PortableDataDirectoryWriteTestError ?? "(unknown)";
                AppLog.Warn(
                    "App",
                    $"便携版 data 目录不可写，已回退到 LocalAppData：data='{AppDataPaths.PortableDataDirectory}', error='{error}'");
            }

            AppLog.Info("App", $"应用启动：version={version}, args='{args.Arguments ?? string.Empty}', logFile='{AppLog.CurrentLogFilePath ?? "(null)"}'");

            _window = new MainWindow();
            _window.Activate();

            try
            {
                if (_window is MainWindow mainWindow)
                {
                    AppErrorService.Instance.Initialize(mainWindow);
                }
            }
            catch (Exception ex)
            {
                // 初始化失败不应阻断启动：记录日志便于排查。
                AppLog.Warn("App", "初始化统一错误处理服务失败", ex);
            }

            // 启动完成后再弹提醒：避免窗口尚未就绪时应用内弹条控件未挂载，导致提醒丢失。
            TryRemindAppDataIssuesIfNeeded(_window);
        }

        private static void TryRemindAppDataIssuesIfNeeded(Window window)
        {
            try
            {
                if (AppDataPaths.InstallKind == AppInstallKind.Portable && !AppDataPaths.UsingPortableDataDirectory)
                {
                    AppReminderService.Instance.RemindOncePerSignature(
                        window,
                        signature: "Data:PortableDataNotWritable",
                        new AppReminderMessage
                        {
                            Title = L10n.Get("Reminder_Data_PortableNotWritable_Title"),
                            Body = L10n.Get("Reminder_Data_PortableNotWritable_Body_Fmt"),
                            Severity = AppReminderSeverity.Warning,
                            ClickAction = AppReminderClickAction.OpenAppDataRootDirectory,
                        });
                }
            }
            catch (Exception ex)
            {
                // 提醒失败不应影响启动：记录日志便于排查。
                AppLog.Warn("Reminders", "启动阶段数据目录提醒失败", ex);
            }
        }

        private static void EnsureAppNotificationInvokedHandlerHooked()
        {
            // 防御：避免重复订阅（理论上 OnLaunched 只会调用一次，但热重载/异常恢复等场景下可能重复进入）。
            if (System.Threading.Interlocked.CompareExchange(ref _notificationInvokedHooked, 1, 0) != 0)
            {
                return;
            }

            try
            {
                AppNotificationManager.Default.NotificationInvoked += OnAppNotificationInvoked;
            }
            catch (Exception ex)
            {
                // 即便 Hook 失败，也不影响启动：只会导致 Toast 点击动作不可用。
                AppLog.Warn("Reminders", "注册 Windows 通知回调失败（点击动作将不可用）", ex);
            }
        }

        private static void OnAppNotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
        {
            try
            {
                if (!AppReminderToastArguments.TryParseClickAction(args, out AppReminderClickAction action))
                {
                    return;
                }

                AppReminderActionExecutor.TryExecute(action);
            }
            catch (Exception ex)
            {
                // 防御：通知回调异常不应影响进程。
                AppLog.Warn("Reminders", "处理 Windows 通知点击动作失败", ex);
            }
        }

        private static void OnAppUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            try
            {
                AppErrorService.Instance.HandleWinUiUnhandledException(e);
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
                AppErrorService.Instance.HandleAppDomainUnhandledException(e);
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
                AppErrorService.Instance.HandleUnobservedTaskException(e);
            }
            catch
            {
                // 兜底：全局异常处理本身不能再抛异常
            }
        }
    }
}
