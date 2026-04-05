using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace WindBoard.Launcher;

internal static partial class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        LauncherTargetInfo target = LauncherTargetResolver.Resolve(AppContext.BaseDirectory);
        if (string.IsNullOrWhiteSpace(target.TargetExecutablePath)
            || string.IsNullOrWhiteSpace(target.WorkingDirectory)
            || !File.Exists(target.TargetExecutablePath))
        {
            ShowLaunchError(target.TargetExecutablePath);
            return 1;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = target.TargetExecutablePath,
                WorkingDirectory = target.WorkingDirectory,
                UseShellExecute = false,
            };

            for (int i = 0; i < args.Length; i++)
            {
                startInfo.ArgumentList.Add(args[i] ?? string.Empty);
            }

            Process? process = Process.Start(startInfo);
            if (process is null)
            {
                ShowLaunchError(target.TargetExecutablePath);
                return 1;
            }

            return 0;
        }
        catch
        {
            ShowLaunchError(target.TargetExecutablePath);
            return 1;
        }
    }

    private static void ShowLaunchError(string targetExecutablePath)
    {
        string path = string.IsNullOrWhiteSpace(targetExecutablePath) ? "(unknown)" : targetExecutablePath;
        _ = MessageBox(IntPtr.Zero, $"无法启动 WindBoard。\n\n未找到主程序：\n{path}", "WindBoard", 0x00000010u);
    }

    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
}
