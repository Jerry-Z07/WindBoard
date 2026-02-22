using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace WindBoard.CrashReporter
{
    /// <summary>
    /// 崩溃提示窗口（WinForms）。
    /// 目标：不依赖 WinUI 视觉树，尽量在主进程异常时仍可向用户展示诊断入口。
    /// </summary>
    internal sealed class CrashReporterForm : Form
    {
        private const int MaxReportBytesToLoad = 1 * 1024 * 1024; // 1MB，避免超大文件导致 UI 卡顿

        private readonly CrashReporterArgs _args;
        private readonly TextBox _summaryTextBox;
        private readonly TextBox _detailsTextBox;
        private readonly Button _toggleDetailsButton;
        private readonly Label _statusLabel;
        private readonly Label _suggestionsLabel;

        internal CrashReporterForm(CrashReporterArgs args)
        {
            _args = args ?? new CrashReporterArgs();

            Text = "WindBoard 崩溃提示";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(width: 760, height: 420);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(12),
            };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 35));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 65));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var title = new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                Font = new Font(Font.FontFamily, 12, FontStyle.Bold),
                Text = "应用遇到未处理异常并即将退出",
            };
            layout.Controls.Add(title, column: 0, row: 0);

            _summaryTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font(Font.FontFamily, 9, FontStyle.Regular),
            };
            layout.Controls.Add(_summaryTextBox, column: 0, row: 1);

            _detailsTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                Visible = false,
                Font = new Font(Font.FontFamily, 9, FontStyle.Regular),
            };
            layout.Controls.Add(_detailsTextBox, column: 0, row: 2);

            var footer = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2,
                Margin = new Padding(0),
                Padding = new Padding(0),
            };
            // 说明：右侧按钮区域使用 AutoSize，避免窗口较窄时按钮被挤压导致文本显示不完整。
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            footer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            footer.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _suggestionsLabel = new Label
            {
                AutoSize = true,
                ForeColor = Color.DimGray,
                Font = new Font(Font.FontFamily, 9, FontStyle.Regular),
                Text = BuildSuggestionsText(),
                Margin = new Padding(0, 0, 0, 2),
            };
            footer.Controls.Add(_suggestionsLabel, column: 0, row: 0);

            _statusLabel = new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                ForeColor = Color.DimGray,
                Text = string.Empty,
                Margin = new Padding(0),
            };
            footer.Controls.Add(_statusLabel, column: 0, row: 1);

            var buttons = new FlowLayoutPanel
            {
                Anchor = AnchorStyles.Right | AnchorStyles.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0),
                Padding = new Padding(0),
            };

            var exitButton = new Button { Text = "退出", AutoSize = true };
            exitButton.Click += (_, _) => Close();
            buttons.Controls.Add(exitButton);

            _toggleDetailsButton = new Button { Text = "显示详情", AutoSize = true };
            _toggleDetailsButton.Click += (_, _) => ToggleDetails();
            buttons.Controls.Add(_toggleDetailsButton);

            var openLogsButton = new Button { Text = "打开日志目录", AutoSize = true };
            openLogsButton.Click += (_, _) => OpenLogsDirectory();
            buttons.Controls.Add(openLogsButton);

            var openReportButton = new Button { Text = "打开崩溃报告", AutoSize = true };
            openReportButton.Click += (_, _) => OpenCrashReport();
            buttons.Controls.Add(openReportButton);

            var copyButton = new Button { Text = "复制诊断信息", AutoSize = true };
            copyButton.Click += (_, _) => CopyDiagnostics();
            buttons.Controls.Add(copyButton);

            footer.Controls.Add(buttons, column: 1, row: 0);
            footer.SetRowSpan(buttons, 2);

            layout.Controls.Add(footer, column: 0, row: 3);

            Controls.Add(layout);

            Load += (_, _) => InitializeFromArgs();
        }

        private void InitializeFromArgs()
        {
            try
            {
                CrashReporterLog.Info(_args.LogsDirectory, $"CrashReporter started: report='{_args.ReportPath}', logsDir='{_args.LogsDirectory}', source='{_args.Source}'");
            }
            catch
            {
                // 忽略：日志失败不影响 UI
            }

            _summaryTextBox.Text = BuildSummaryText(_args);

            // 预加载详情（但默认折叠显示）
            _detailsTextBox.Text = TryLoadReportText(_args.ReportPath, out bool truncated);
            if (truncated)
            {
                _statusLabel.Text = "报告内容过长，已截断显示。";
            }
        }

        private void ToggleDetails()
        {
            try
            {
                bool show = !_detailsTextBox.Visible;
                _detailsTextBox.Visible = show;
                _toggleDetailsButton.Text = show ? "隐藏详情" : "显示详情";
            }
            catch (Exception ex)
            {
                CrashReporterLog.Warn(_args.LogsDirectory, "切换详情显示失败", ex);
                _statusLabel.Text = "切换详情失败。";
            }
        }

        private void CopyDiagnostics()
        {
            try
            {
                string text = _detailsTextBox.Text;
                if (string.IsNullOrWhiteSpace(text))
                {
                    text = BuildFallbackDiagnosticsText(_args);
                }

                Clipboard.SetText(text);
                _statusLabel.Text = "已复制到剪贴板。";
            }
            catch (Exception ex)
            {
                CrashReporterLog.Warn(_args.LogsDirectory, "复制诊断信息失败", ex);
                _statusLabel.Text = "复制失败。";
            }
        }

        private void OpenLogsDirectory()
        {
            try
            {
                string dir = (_args.LogsDirectory ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(dir))
                {
                    _statusLabel.Text = "日志目录为空。";
                    return;
                }

                if (!Directory.Exists(dir))
                {
                    _statusLabel.Text = "日志目录不存在。";
                    return;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = dir,
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                CrashReporterLog.Warn(_args.LogsDirectory, "打开日志目录失败", ex);
                _statusLabel.Text = "打开日志目录失败。";
            }
        }

        private void OpenCrashReport()
        {
            try
            {
                string path = (_args.ReportPath ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(path))
                {
                    _statusLabel.Text = "崩溃报告路径为空。";
                    return;
                }

                if (!File.Exists(path))
                {
                    _statusLabel.Text = "崩溃报告文件不存在。";
                    return;
                }

                // 使用 /select 让用户更容易定位到文件
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{path}\"",
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                CrashReporterLog.Warn(_args.LogsDirectory, "打开崩溃报告失败", ex);
                _statusLabel.Text = "打开崩溃报告失败。";
            }
        }

        private static string BuildSummaryText(CrashReporterArgs args)
        {
            // 说明：摘要信息尽量简短，便于截图与用户理解。
            var sb = new StringBuilder(capacity: 512);
            sb.AppendLine("请将以下信息发送给开发者以协助定位问题：");
            sb.AppendLine();
            sb.AppendLine($"来源：{(string.IsNullOrWhiteSpace(args.Source) ? "(unknown)" : args.Source)}");
            sb.AppendLine($"崩溃报告：{(string.IsNullOrWhiteSpace(args.ReportPath) ? "(none)" : args.ReportPath)}");
            sb.AppendLine($"日志目录：{(string.IsNullOrWhiteSpace(args.LogsDirectory) ? "(none)" : args.LogsDirectory)}");
            return sb.ToString();
        }

        private static string BuildSuggestionsText()
        {
            // 说明：建议操作放在窗口左下角，避免摘要区域过长且便于用户随时看到。
            return
                "建议操作：" + Environment.NewLine +
                "- 点击“复制诊断信息”复制文本" + Environment.NewLine +
                "- 点击“打开日志目录”打开发送日志文件" + Environment.NewLine +
                "- 点击“显示详情”查看/复制崩溃报告全文";
        }

        private static string BuildFallbackDiagnosticsText(CrashReporterArgs args)
        {
            // 兜底：详情为空时至少让用户复制到“路径信息”，便于排查。
            string report = (args.ReportPath ?? string.Empty).Trim();
            string logs = (args.LogsDirectory ?? string.Empty).Trim();
            string source = (args.Source ?? string.Empty).Trim();
            return $"source='{source}', report='{report}', logsDir='{logs}'";
        }

        private static string TryLoadReportText(string reportPath, out bool truncated)
        {
            truncated = false;
            try
            {
                string path = (reportPath ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(path))
                {
                    return string.Empty;
                }

                if (!File.Exists(path))
                {
                    return $"(崩溃报告不存在：'{path}')";
                }

                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                long length = 0;
                try { length = fs.Length; } catch { length = 0; }

                int toRead = length > MaxReportBytesToLoad ? MaxReportBytesToLoad : (int)Math.Max(0, Math.Min(length, int.MaxValue));
                if (length > MaxReportBytesToLoad)
                {
                    truncated = true;
                }

                // UTF-8 写入；读取时也按 UTF-8 尝试（即使失败也不抛异常）
                byte[] buffer = new byte[toRead];
                int read = fs.Read(buffer, 0, toRead);
                string text = Encoding.UTF8.GetString(buffer, 0, read);
                if (truncated)
                {
                    text += Environment.NewLine + Environment.NewLine + "(...内容过长，已截断...)";
                }

                return text;
            }
            catch (Exception ex)
            {
                return $"(读取崩溃报告失败：{ex.GetType().Name}: {ex.Message})";
            }
        }
    }
}
