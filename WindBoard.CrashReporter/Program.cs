using System;
using System.Windows.Forms;

namespace WindBoard.CrashReporter
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            CrashReporterArgs parsed;
            try
            {
                parsed = CrashReporterArgs.Parse(args);
            }
            catch
            {
                // 兜底：解析失败也不能阻断启动
                parsed = new CrashReporterArgs();
            }

            try
            {
                ApplicationConfiguration.Initialize();
                Application.Run(new CrashReporterForm(parsed));
            }
            catch (Exception ex)
            {
                // 终极兜底：如果 UI 启动失败，尽力记录到日志目录，避免完全“静默”。
                try
                {
                    CrashReporterLog.Error(parsed.LogsDirectory, "CrashReporter 启动失败", ex);
                }
                catch
                {
                    // 忽略
                }
            }
        }
    }
}

