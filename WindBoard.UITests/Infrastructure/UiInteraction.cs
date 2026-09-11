using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;

namespace WindBoard.UITests.Infrastructure
{
    /// <summary>
    /// UI 交互/查找基元：点击、开关切换、文本写入、窗口/按钮定位。
    /// 断言优先走 AutomationId；仅系统/应用弹窗按钮使用 Name 匹配（文案集中在 UiText）。
    /// </summary>
    public static class UiInteraction
    {
        /// <summary>限深查找指定 AutomationId 的后代元素。</summary>
        public static AutomationElement? FindByAutomationIdLimitedDepth(AutomationElement root, string automationId, int maxDepth)
        {
            var frontier = new List<AutomationElement> { root };

            for (int depth = 0; depth < maxDepth && frontier.Count > 0; depth++)
            {
                var next = new List<AutomationElement>();

                foreach (AutomationElement element in frontier)
                {
                    AutomationElement[] children;
                    try
                    {
                        children = element.FindAllChildren();
                    }
                    catch
                    {
                        continue;
                    }

                    foreach (AutomationElement child in children)
                    {
                        try
                        {
                            if (string.Equals(child.Properties.AutomationId.ValueOrDefault, automationId, StringComparison.Ordinal))
                            {
                                return child;
                            }

                            next.Add(child);
                        }
                        catch
                        {
                            // 属性读取失败（窗口销毁中）：跳过。
                        }
                    }
                }

                frontier = next;
            }

            return null;
        }

        public static void ClickCenter(AutomationElement element)
        {
            Rectangle rect = element.Properties.BoundingRectangle.Value;
            Mouse.Click(new Point(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2), MouseButton.Left);
        }

        /// <summary>
        /// 激活元素：优先 InvokePattern（不经鼠标、不依赖窗口在前台——E2E 的被测窗口
        /// 由后台进程启动，常被前台锁压在其它应用之下）；无 Invoke 模式或取模式异常
        /// （如自定义 peer 声明与实现不符抛 InvalidCastException）时回退鼠标点击。
        /// </summary>
        public static void InvokeOrClick(AutomationElement element)
        {
            try
            {
                var invoke = element.Patterns.Invoke.PatternOrDefault;
                if (invoke is not null)
                {
                    invoke.Invoke();
                    return;
                }
            }
            catch
            {
                // pattern 获取/调用异常：走鼠标点击兜底。
            }

            ClickCenter(element);
        }

        /// <summary>
        /// 经 MSAA LegacyIAccessible.DoDefaultAction 触发元素默认动作（provider 侧执行，零坐标依赖），
        /// 失败时回退鼠标点击。用于 ContentDialog 按钮（其 InvokePattern 实测不触发应用逻辑）。
        /// </summary>
        public static void InvokeLegacyOrClick(AutomationElement element)
        {
            try
            {
                var legacy = element.Patterns.LegacyIAccessible.PatternOrDefault;
                if (legacy is not null)
                {
                    legacy.DoDefaultAction();
                    return;
                }
            }
            catch
            {
                // Legacy 模式不可用：回退鼠标点击。
            }

            ClickCenter(element);
        }

        /// <summary>
        /// 把窗口强制带到前台。
        /// 说明：E2E 由后台进程（testhost）启动被测应用，Windows 前台锁（SetForegroundWindow
        /// 的调用限制）会把新窗口压在当前前台应用之下；而系统文件选择器（IFileDialog）只在
        /// 宿主窗口激活时才显示，ContentDialog 按钮的鼠标点击也需要窗口无遮挡。
        /// 采用经典 ALT 键技巧：先模拟一次 ALT 按下/抬起解除前台锁，再调用 Focus。
        /// </summary>
        public static void BringToFront(Window window)
        {
            try
            {
                Keyboard.Press(VirtualKeyShort.ALT);
                Keyboard.Release(VirtualKeyShort.ALT);
                window.Focus();
            }
            catch
            {
                // 前台化失败不阻断用例：程序性 UIA 交互（Toggle/SelectionItem）仍可工作。
            }
        }

        /// <summary>读取 ToggleSwitch/ToggleButton 当前状态。</summary>
        public static bool GetToggleState(AutomationElement element)
        {
            var pattern = element.Patterns.Toggle.PatternOrDefault
                ?? throw new InvalidOperationException("元素不支持 Toggle 模式。");
            return pattern.ToggleState == ToggleState.On;
        }

        /// <summary>把开关切换到目标状态（已处于目标状态时不动）。</summary>
        public static void SetToggle(AutomationElement element, bool targetOn)
        {
            var pattern = element.Patterns.Toggle.PatternOrDefault
                ?? throw new InvalidOperationException("元素不支持 Toggle 模式。");

            if ((pattern.ToggleState == ToggleState.On) != targetOn)
            {
                pattern.Toggle();
            }
        }

        /// <summary>发送 Enter 键（触发 ContentDialog 的 DefaultButton=Primary）。</summary>
        public static void PressEnter()
        {
            Keyboard.Press(VirtualKeyShort.RETURN);
            Keyboard.Release(VirtualKeyShort.RETURN);
        }

        /// <summary>通过 Value 模式写入文本（不经键盘注入，规避 WinUI send-keys 失效问题）。</summary>
        public static void SetValue(AutomationElement edit, string value)
        {
            var pattern = edit.Patterns.Value.PatternOrDefault
                ?? throw new InvalidOperationException("元素不支持 Value 模式。");
            pattern.SetValue(value);
        }

        /// <summary>
        /// 限深广度优先查找指定 ControlType 的可见后代。
        /// 背景：Win11 文件对话框对全深度 FindAllDescendants 会触发 UIA 内部超时
        /// （UIA_E_TIMEOUT，约 12.5s），浅层逐层遍历可规避。
        /// </summary>
        public static AutomationElement? FindVisibleDescendantLimitedDepth(
            AutomationElement root,
            ControlType targetType,
            int maxDepth)
        {
            var frontier = new List<AutomationElement> { root };

            for (int depth = 0; depth < maxDepth && frontier.Count > 0; depth++)
            {
                var next = new List<AutomationElement>();

                foreach (AutomationElement element in frontier)
                {
                    AutomationElement[] children;
                    try
                    {
                        children = element.FindAllChildren();
                    }
                    catch
                    {
                        continue;
                    }

                    foreach (AutomationElement child in children)
                    {
                        try
                        {
                            if (child.Properties.ControlType.ValueOrDefault == targetType
                                && !child.Properties.IsOffscreen.ValueOrDefault)
                            {
                                return child;
                            }

                            next.Add(child);
                        }
                        catch
                        {
                            // 属性读取失败（窗口销毁中）：跳过。
                        }
                    }
                }

                frontier = next;
            }

            return null;
        }

        /// <summary>
        /// 限深广度优先查找按显示文案匹配的按钮（WinUI 弹层 HWND 的 UIA 子树浅层遍历，
        /// 规避全树条件搜索的 UIA 内部超时问题）。文案用前缀匹配（兼容“打开(O)”类快捷键后缀）。
        /// </summary>
        public static AutomationElement? FindButtonByNameLimitedDepth(
            AutomationElement root,
            string[] names,
            int maxDepth)
        {
            var frontier = new List<AutomationElement> { root };

            for (int depth = 0; depth < maxDepth && frontier.Count > 0; depth++)
            {
                var next = new List<AutomationElement>();

                foreach (AutomationElement element in frontier)
                {
                    AutomationElement[] children;
                    try
                    {
                        children = element.FindAllChildren();
                    }
                    catch
                    {
                        continue;
                    }

                    foreach (AutomationElement child in children)
                    {
                        try
                        {
                            if (child.Properties.ControlType.ValueOrDefault == ControlType.Button)
                            {
                                string name = child.Properties.Name.ValueOrDefault ?? string.Empty;
                                if (names.Any(candidate => name.StartsWith(candidate, StringComparison.Ordinal)))
                                {
                                    return child;
                                }
                            }
                            else
                            {
                                next.Add(child);
                            }
                        }
                        catch
                        {
                            // 属性读取失败（窗口销毁中）：跳过。
                        }
                    }
                }

                frontier = next;
            }

            return null;
        }

        /// <summary>在窗口/对话框内按显示文本找按钮，找不到时回退经典对话框 AutomationId（为空白则不回退）。</summary>
        public static AutomationElement? TryFindDialogButton(
            AutomationElement container,
            ConditionFactory cf,
            string[] names,
            string fallbackAutomationId)
        {
            foreach (string name in names)
            {
                var condition = new AndCondition(cf.ByName(name), cf.ByControlType(ControlType.Button));
                AutomationElement? found = TryFind(() => container.FindFirstDescendant(condition));
                if (found is not null)
                {
                    return found;
                }
            }

            if (string.IsNullOrWhiteSpace(fallbackAutomationId))
            {
                // 空的 ByAutomationId("") 会匹配树上任意无 AutomationId 元素，产生误命中，禁止。
                return null;
            }

            return TryFind(() => container.FindFirstDescendant(cf.ByAutomationId(fallbackAutomationId)));
        }

        /// <summary>在应用进程的全部可见顶层窗口中按条件查找（设置窗口/系统文件对话框等）。</summary>
        public static Window? TryFindTopLevelWindow(WindBoardApp app, Func<Window, bool> predicate)
        {
            var desktop = app.Automation.GetDesktop();
            var condition = new AndCondition(app.Cf.ByProcessId(app.ProcessId), app.Cf.ByControlType(ControlType.Window));

            foreach (AutomationElement element in desktop.FindAllDescendants(condition))
            {
                try
                {
                    var window = element.AsWindow();
                    if (window.Properties.IsOffscreen.ValueOrDefault)
                    {
                        continue;
                    }

                    if (predicate(window))
                    {
                        return window;
                    }
                }
                catch
                {
                    // 个别窗口句柄正在销毁：跳过。
                }
            }

            return null;
        }

        /// <summary>
        /// 查找系统文件对话框。
        /// Win11 的新版 IFileDialog 不出现在 UIA 桌面树的条件搜索结果中，且对话框由
        /// Shell 侧进程承载（pid 与被测应用不同，实测），因此用 Win32 EnumWindows 按
        /// “owner 为主窗口”定位句柄（标题匹配兜底），再转成 FlaUI 窗口。
        /// </summary>
        public static Window? TryFindFileDialog(WindBoardApp app)
        {
            nint mainHwnd = app.MainWindow.Properties.NativeWindowHandle.ValueOrDefault;
            nint? handle = NativeWindowFinder.FindFileDialogHandle(mainHwnd, FileDialogTitles);

            if (handle is null)
            {
                return null;
            }

            try
            {
                return app.Automation.FromHandle(handle.Value).AsWindow();
            }
            catch
            {
                return null;
            }
        }

        private static readonly string[] FileDialogTitles = { "另存为", "保存", "打开", "Save As", "Save", "Open" };

        /// <summary>
        /// 可见顶层窗口概览（诊断用，Win32 枚举）：输出 pid/class/标题，
        /// 用于文件对话框等系统窗口定位失败时确认真实窗口归属。
        /// </summary>
        public static string DumpDesktopWindows(WindBoardApp app)
        {
            var summary = new System.Text.StringBuilder();
            summary.AppendLine(CultureInfo.InvariantCulture, $"---- 可见顶层窗口（被测 pid={app.ProcessId}）----");
            try
            {
                foreach (nint hWnd in NativeWindowFinder.EnumVisibleTopLevelWindows())
                {
                    uint pid = NativeWindowFinder.GetProcessId(hWnd);
                    summary.AppendLine(
                        CultureInfo.InvariantCulture,
                        $"pid={pid} "
                        + $"class='{NativeWindowFinder.GetClassName(hWnd)}' "
                        + $"title='{NativeWindowFinder.GetWindowTitle(hWnd)}'");
                }
            }
            catch (Exception ex)
            {
                summary.AppendLine(CultureInfo.InvariantCulture, $"枚举失败：{ex.Message}");
            }

            return summary.ToString();
        }

        /// <summary>吞掉 COM/树重建期间的单次查找异常，交给上层 retry。</summary>
        private static AutomationElement? TryFind(Func<AutomationElement?> finder)
        {
            try
            {
                return finder();
            }
            catch
            {
                return null;
            }
        }
    }
}
