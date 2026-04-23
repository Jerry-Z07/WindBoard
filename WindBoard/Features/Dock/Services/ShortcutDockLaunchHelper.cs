using System;
using System.Diagnostics;
using System.IO;

namespace WindBoard.Features.Dock.Services
{
    /// <summary>
    /// 快捷入口 Dock：启动目标的解析与归一化（纯字符串逻辑，便于单元测试覆盖）。
    /// </summary>
    internal static class ShortcutDockLaunchHelper
    {
        internal static void NormalizeProgramLaunch(string? input, string? arguments, out string target, out string args)
        {
            target = NormalizeInput(input);
            args = NormalizeArguments(arguments);

            if (string.IsNullOrWhiteSpace(target))
            {
                return;
            }

            // 兼容用户把“启动参数”一起输入到路径中：尝试从命令行里拆出路径与参数。
            if (TrySplitProgramCommandLine(target, out string splitTarget, out string splitArgs))
            {
                if (!string.IsNullOrWhiteSpace(splitTarget))
                {
                    target = splitTarget;
                }

                if (string.IsNullOrWhiteSpace(args) && !string.IsNullOrWhiteSpace(splitArgs))
                {
                    args = NormalizeArguments(splitArgs);
                }
            }
        }

        internal static string NormalizeInput(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return string.Empty;
            }

            string value = input.Trim();

            // 兼容用户手动输入带引号的路径，例如："C:\Program Files\App\app.exe"
            if (value.Length >= 2)
            {
                if ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\''))
                {
                    value = value[1..^1].Trim();
                }
            }

            // 展开环境变量，例如：%USERPROFILE%\Desktop
            value = Environment.ExpandEnvironmentVariables(value);
            return value.Trim();
        }

        internal static string NormalizeArguments(string? arguments)
        {
            if (string.IsNullOrWhiteSpace(arguments))
            {
                return string.Empty;
            }

            // 参数一般不需要去引号（参数本身可能含引号），这里只做环境变量展开与首尾空白处理。
            string value = Environment.ExpandEnvironmentVariables(arguments);
            return value.Trim();
        }

        internal static bool TryNormalizeLinkUri(string? input, out Uri? uri)
        {
            uri = null;

            string value = NormalizeInput(input);
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            // 先尝试按“完整 URI”解析（支持 http/https/mailto 等）。
            if (Uri.TryCreate(value, UriKind.Absolute, out Uri? direct))
            {
                uri = direct;
                return true;
            }

            // 兼容用户只输入 host/path，例如：example.com 或 example.com/a
            // 这里默认补 https。
            if (!value.Contains("://", StringComparison.Ordinal)
                && Uri.TryCreate("https://" + value, UriKind.Absolute, out Uri? https))
            {
                uri = https;
                return true;
            }

            return false;
        }

        internal static ProcessStartInfo CreateProgramProcessStartInfo(string targetPath, string? arguments)
        {
            string target = NormalizeInput(targetPath);
            string args = NormalizeArguments(arguments);

            string ext;
            try
            {
                ext = Path.GetExtension(target).ToLowerInvariant();
            }
            catch
            {
                ext = string.Empty;
            }

            // 说明：
            // - .exe：UseShellExecute=false 使用 CreateProcess，更稳定且参数行为更可控；
            // - .bat/.cmd：通过 cmd.exe /c 执行，确保参数可用；
            // - .lnk：交给 Shell 打开（参数不保证生效）。
            if (string.Equals(ext, ".exe", StringComparison.Ordinal))
            {
                return new ProcessStartInfo(target)
                {
                    UseShellExecute = false,
                    Arguments = args,
                    WorkingDirectory = Path.GetDirectoryName(target) ?? string.Empty,
                };
            }

            if (string.Equals(ext, ".bat", StringComparison.Ordinal) || string.Equals(ext, ".cmd", StringComparison.Ordinal))
            {
                // cmd.exe /c ""C:\a\b.cmd" arg1 arg2"
                string command = $"\"{target}\"";
                if (!string.IsNullOrWhiteSpace(args))
                {
                    command += " " + args;
                }

                return new ProcessStartInfo("cmd.exe")
                {
                    UseShellExecute = false,
                    Arguments = "/c \"" + command + "\"",
                    WorkingDirectory = Path.GetDirectoryName(target) ?? string.Empty,
                };
            }

            return new ProcessStartInfo(target)
            {
                UseShellExecute = true,
                // ShellExecute 对 .lnk 等文件的参数支持不可靠：这里仍保留赋值，
                // 以便对支持的目标类型（例如某些可执行包装器）生效。
                Arguments = args,
                WorkingDirectory = Path.GetDirectoryName(target) ?? string.Empty,
            };
        }

        private static bool TrySplitProgramCommandLine(string input, out string target, out string arguments)
        {
            target = input;
            arguments = string.Empty;

            string value = input.Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            // 情况 1：带引号的路径 + 参数，例如："C:\Program Files\App\app.exe" --foo
            if (value[0] == '\"' || value[0] == '\'')
            {
                char quote = value[0];
                int endQuote = value.IndexOf(quote, 1);
                if (endQuote > 1)
                {
                    target = value.Substring(1, endQuote - 1).Trim();
                    arguments = value.Substring(endQuote + 1).Trim();
                    return true;
                }
            }

            // 情况 2：未加引号的路径 + 参数，例如：C:\App\app.exe --foo
            if (TrySplitByKnownExtension(value, out string splitTarget, out string splitArgs))
            {
                target = splitTarget;
                arguments = splitArgs;
                return true;
            }

            return false;
        }

        private static bool TrySplitByKnownExtension(string input, out string target, out string arguments)
        {
            target = input;
            arguments = string.Empty;

            string[] extensions = [".exe", ".bat", ".cmd", ".lnk"];
            foreach (string extension in extensions)
            {
                int index = 0;
                while (index >= 0)
                {
                    index = input.IndexOf(extension, index, StringComparison.OrdinalIgnoreCase);
                    if (index < 0)
                    {
                        break;
                    }

                    int end = index + extension.Length;
                    if (end == input.Length || char.IsWhiteSpace(input[end]))
                    {
                        target = input[..end].TrimEnd();
                        arguments = input[end..].Trim();
                        return true;
                    }

                    index = end;
                }
            }

            return false;
        }
    }
}
