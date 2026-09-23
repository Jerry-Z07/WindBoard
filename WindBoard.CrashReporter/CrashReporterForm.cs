using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace WindBoard.CrashReporter
{
    /// <summary>
    /// 崩溃提示窗口（WinForms）。
    /// 目标：不依赖 WinUI 视觉树，尽量在主进程异常时仍可向用户展示诊断入口。
    ///
    /// 高 DPI 布局约定（依据 research/winforms-high-dpi.md §7 的 150% 实测结论）：
    /// - 缩放自管：使用 AutoScaleMode.None，所有固定像素经 LogicalToDeviceUnits 换算。
    ///   实测 Dpi / Font 模式下的 AutoScaleDimensions 会被框架归一化到当前 DPI，
    ///   缩放因子恒为 1，无法触发初始缩放；
    /// - 字号使用 pt（由 GDI+ 按设备 DPI 换算，物理尺寸天然正确）；
    /// - TableLayoutPanel 行样式语义固定：Absolute → AutoSize → Percent；
    /// - 可换行文本一律不放入 AutoSize 行（内容膨胀会直接挤占 Percent 行）。
    /// </summary>
    internal sealed class CrashReporterForm : Form
    {
        private const int MaxReportBytesToLoad = 1 * 1024 * 1024; // 1MB，避免超大文件导致 UI 卡顿

        // 摘要区固定高度（96 DPI 逻辑像素）：容纳固定 4 行摘要，并为异常消息换行留出余量。
        private const int SummaryGroupHeight = 132;

        private const string UnknownPlaceholder = "(unknown)";

        private readonly CrashReporterArgs _args;
        private readonly TextBox _summaryTextBox;
        private readonly TextBox _reportTextBox;
        private readonly Label _statusLabel;

        internal CrashReporterForm(CrashReporterArgs args)
        {
            _args = args ?? new CrashReporterArgs();

            // 说明：实测表明 AutoScaleDimensions 会被框架归一化到当前 DPI，使 Dpi / Font
            // 两种模式的缩放因子恒为 1；故禁用自动缩放，由本类对固定像素做显式换算。
            // 全部逻辑值按 96 DPI 书写，设备值 = 逻辑值 × (当前 DPI / 96)。
            AutoScaleMode = AutoScaleMode.None;

            Text = "WindBoard 崩溃提示";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = LogicalToDeviceUnits(new Size(width: 900, height: 640));
            MinimumSize = LogicalToDeviceUnits(new Size(width: 780, height: 540));

            _summaryTextBox = CreateSummaryTextBox();
            _reportTextBox = CreateReportTextBox();
            _statusLabel = CreateStatusLabel();

            Controls.Add(CreateMainLayout());

            Load += (_, _) => InitializeFromArgs();
        }

        private TableLayoutPanel CreateMainLayout()
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = ScalePadding(new Padding(12)),
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            // 行样式语义（固定不随内容变化）：
            // - 标题区 / 底部区：AutoSize，内容为固定文案 + 单行状态行 + 不换行按钮行；
            // - 摘要区：Absolute，恒为 4 行摘要，高度可预测；
            // - 报告区：唯一 Percent 行，长内容吃满剩余空间。
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, LogicalToDeviceUnits(SummaryGroupHeight)));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            layout.Controls.Add(CreateHeader(), column: 0, row: 0);
            layout.Controls.Add(CreateGroupBox(caption: "错误信息", content: _summaryTextBox), column: 0, row: 1);
            layout.Controls.Add(CreateGroupBox(caption: "完整崩溃报告", content: _reportTextBox), column: 0, row: 2);
            layout.Controls.Add(CreateFooter(), column: 0, row: 3);

            return layout;
        }

        private TableLayoutPanel CreateHeader()
        {
            var header = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 2,
                Margin = new Padding(0),
                Padding = new Padding(0),
            };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            header.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            // 说明：标题与副标题为固定短文案，最小窗口尺寸下也不会换行，放在 AutoSize 行是安全的。
            var title = new Label
            {
                AutoSize = true,
                Font = new Font(Font.FontFamily, 12F, FontStyle.Bold),
                Text = "应用遇到未处理异常并即将退出",
                Margin = ScalePadding(new Padding(0, 0, 0, 2)),
            };
            header.Controls.Add(title, column: 0, row: 0);

            var subtitle = new Label
            {
                AutoSize = true,
                Font = new Font(Font.FontFamily, 9F, FontStyle.Regular),
                ForeColor = Color.DimGray,
                Text = "请复制错误信息并发送给开发者以协助定位问题",
                Margin = ScalePadding(new Padding(0, 0, 0, 8)),
            };
            header.Controls.Add(subtitle, column: 0, row: 1);

            return header;
        }

        private GroupBox CreateGroupBox(string caption, Control content)
        {
            var group = new GroupBox
            {
                Dock = DockStyle.Fill,
                Text = caption,
                Margin = ScalePadding(new Padding(0, 0, 0, 8)),
            };
            group.Controls.Add(content);

            return group;
        }

        private TextBox CreateSummaryTextBox()
        {
            // 摘要区：固定 4 行字段 + 可能换行的异常消息，垂直滚动兜底，内容始终可选中复制。
            TextBox box = CreateContentTextBox();
            box.WordWrap = true;
            box.ScrollBars = ScrollBars.Vertical;
            box.Font = new Font(Font.FontFamily, 9F, FontStyle.Regular);

            return box;
        }

        private TextBox CreateReportTextBox()
        {
            // 报告区：完整报告需保留原始换行与缩进，故关闭自动换行，使用等宽字体 + 双向滚动。
            TextBox box = CreateContentTextBox();
            box.WordWrap = false;
            box.ScrollBars = ScrollBars.Both;
            box.Font = new Font(FontFamily.GenericMonospace, 9F, FontStyle.Regular);

            return box;
        }

        private TextBox CreateContentTextBox()
        {
            return new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                Margin = new Padding(0),
            };
        }

        private Label CreateStatusLabel()
        {
            // 说明：状态行只承载一行文案，故意用 Dock=Fill + AutoSize=false + AutoEllipsis，
            // 避免文本宽度撑大底部 AutoSize 行（历史上 Label 的 Dock 与 AutoSize 冲突即源于此）。
            return new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = false,
                AutoEllipsis = true,
                ForeColor = Color.DimGray,
                Font = new Font(Font.FontFamily, 9F, FontStyle.Regular),
                Text = string.Empty,
                Margin = new Padding(0),
            };
        }

        private TableLayoutPanel CreateFooter()
        {
            var footer = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 2,
                Margin = new Padding(0),
                Padding = new Padding(0),
            };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            footer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            footer.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            footer.Controls.Add(_statusLabel, column: 0, row: 0);

            // 按钮区：WrapContents=false 保证始终单行排列，AutoSize 让宽度贴合按钮总宽。
            var buttons = new FlowLayoutPanel
            {
                Anchor = AnchorStyles.Right,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = ScalePadding(new Padding(0, 4, 0, 0)),
                Padding = new Padding(0),
            };

            // 说明：FlowDirection.RightToLeft 下先加入者位于最右侧，
            // 视觉顺序为 [复制诊断信息][打开日志目录][打开崩溃报告][退出]。
            var exitButton = new Button { Text = "退出", AutoSize = true };
            exitButton.Click += (_, _) => Close();
            buttons.Controls.Add(exitButton);

            var openReportButton = new Button { Text = "打开崩溃报告", AutoSize = true };
            openReportButton.Click += (_, _) => OpenCrashReport();
            buttons.Controls.Add(openReportButton);

            var openLogsButton = new Button { Text = "打开日志目录", AutoSize = true };
            openLogsButton.Click += (_, _) => OpenLogsDirectory();
            buttons.Controls.Add(openLogsButton);

            var copyButton = new Button { Text = "复制诊断信息", AutoSize = true };
            copyButton.Click += (_, _) => CopyDiagnostics();
            buttons.Controls.Add(copyButton);

            footer.Controls.Add(buttons, column: 0, row: 1);

            return footer;
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
            _reportTextBox.Text = TryLoadReportText(_args.ReportPath, out bool truncated);
            if (truncated)
            {
                _statusLabel.Text = "报告内容过长，已截断显示。";
            }
        }

        private void SafeUiAction(string statusMessage, string logMessage, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                try
                {
                    CrashReporterLog.Warn(_args.LogsDirectory, logMessage, ex);
                }
                catch
                {
                    // 忽略：日志失败不影响 UI
                }

                _statusLabel.Text = statusMessage;
            }
        }

        private void CopyDiagnostics()
        {
            SafeUiAction("复制失败。", "复制诊断信息失败", () =>
            {
                string text = _reportTextBox.Text;
                if (string.IsNullOrWhiteSpace(text))
                {
                    text = BuildFallbackDiagnosticsText(_args);
                }

                Clipboard.SetText(text);
                _statusLabel.Text = "已复制到剪贴板。";
            });
        }

        private void OpenLogsDirectory()
        {
            SafeUiAction("打开日志目录失败。", "打开日志目录失败", () =>
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
            });
        }

        private void OpenCrashReport()
        {
            SafeUiAction("打开崩溃报告失败。", "打开崩溃报告失败", () =>
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
            });
        }

        /// <summary>
        /// 构建摘要区文本（纯函数，不访问控件与文件，便于单测）。
        /// 固定 4 行：异常类型 / 异常消息 / 发生时间 / 崩溃来源；字段为空时输出 <see cref="UnknownPlaceholder"/>。
        /// </summary>
        internal static string BuildSummaryText(CrashReporterArgs args)
        {
            CrashReporterArgs effective = args ?? new CrashReporterArgs();

            // CA1305：显式传入 CurrentCulture（与默认插值行为一致），值均为字符串无格式化差异。
            var sb = new StringBuilder(capacity: 512);
            sb.AppendLine(CultureInfo.CurrentCulture, $"异常类型：{OrUnknown(effective.ExceptionType)}");
            sb.AppendLine(CultureInfo.CurrentCulture, $"异常消息：{OrUnknown(effective.ExceptionMessage)}");
            sb.AppendLine(CultureInfo.CurrentCulture, $"发生时间：{OrUnknown(effective.OccurredAt)}");
            sb.AppendLine(CultureInfo.CurrentCulture, $"崩溃来源：{OrUnknown(effective.Source)}");

            return sb.ToString();
        }

        /// <summary>
        /// 报告区为空时供「复制诊断信息」使用的兜底文本（纯函数，便于单测）。
        /// </summary>
        internal static string BuildFallbackDiagnosticsText(CrashReporterArgs args)
        {
            // 兜底：报告为空时至少让用户复制到“摘要字段 + 路径信息”，便于排查。
            CrashReporterArgs effective = args ?? new CrashReporterArgs();

            // CA1305：显式传入 CurrentCulture（与默认插值行为一致），值均为字符串无格式化差异。
            var sb = new StringBuilder(capacity: 512);
            sb.AppendLine(
                CultureInfo.CurrentCulture,
                $"source='{TrimOrEmpty(effective.Source)}', report='{TrimOrEmpty(effective.ReportPath)}', logsDir='{TrimOrEmpty(effective.LogsDirectory)}'");
            sb.AppendLine(
                CultureInfo.CurrentCulture,
                $"occurredAt='{TrimOrEmpty(effective.OccurredAt)}', exceptionType='{TrimOrEmpty(effective.ExceptionType)}', exceptionMessage='{TrimOrEmpty(effective.ExceptionMessage)}'");

            return sb.ToString();
        }

        /// <summary>
        /// 将 96 DPI 逻辑像素的 <see cref="Padding"/> 换算为设备像素。
        /// 说明：<see cref="Control.LogicalToDeviceUnits(int)"/> 没有 Padding 重载，故在此逐边换算。
        /// </summary>
        private Padding ScalePadding(Padding logicalPadding)
        {
            return new Padding(
                LogicalToDeviceUnits(logicalPadding.Left),
                LogicalToDeviceUnits(logicalPadding.Top),
                LogicalToDeviceUnits(logicalPadding.Right),
                LogicalToDeviceUnits(logicalPadding.Bottom));
        }

        private static string OrUnknown(string? value)
        {
            string trimmed = TrimOrEmpty(value);
            return trimmed.Length == 0 ? UnknownPlaceholder : trimmed;
        }

        private static string TrimOrEmpty(string? value)
        {
            return (value ?? string.Empty).Trim();
        }

        private static string TryLoadReportText(string reportPath, out bool truncated)
        {
            truncated = false;
            string path = (reportPath ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            if (!File.Exists(path))
            {
                return $"(崩溃报告不存在：'{path}')";
            }

            try
            {
                return ReadFileWithLimit(path, MaxReportBytesToLoad, out truncated);
            }
            catch (Exception ex)
            {
                return $"(读取崩溃报告失败：{ex.GetType().Name}: {ex.Message})";
            }
        }

        private static string ReadFileWithLimit(string path, int maxBytes, out bool truncated)
        {
            truncated = false;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            long length = 0;
            try { length = fs.Length; } catch { length = 0; }

            int toRead = length > maxBytes ? maxBytes : (int)Math.Max(0, Math.Min(length, int.MaxValue));
            if (length > maxBytes)
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
    }
}
