using System;
using System.Linq;
using FlaUI.Core.AutomationElements;
using WindBoard.UITests.Infrastructure;

namespace WindBoard.UITests
{
    /// <summary>
    /// 多条场景共享的导航步骤（打开设置窗口/切换设置页/驱动系统文件对话框）。
    /// </summary>
    public static class ScenarioSteps
    {
        /// <summary>打开设置窗口：主窗口“更多”菜单 → 设置。</summary>
        public static Window OpenSettingsWindow(WindBoardApp app)
        {
            // 菜单弹出与后续系统对话框均要求主窗口在前台。
            UiInteraction.BringToFront(app.MainWindow);

            var moreButton = UiWait.ForElement(
                () => TryFind(app, () => app.MainWindow.FindFirstDescendant(app.Cf.ByAutomationId("Dock_MoreButton"))),
                "主窗口 Dock 的“更多”按钮（Dock_MoreButton）");
            UiInteraction.InvokeOrClick(moreButton);

            // MenuFlyout 是独立顶层弹层：从桌面全局按 AutomationId 查找。
            // 全局深搜在桌面树较大时单次可能耗时数秒，给足等待时间。
            var settingsItem = UiWait.ForElement(
                () => TryFindEverywhere(app, "Dock_SettingsMenuItem"),
                "更多菜单中的“设置”项（Dock_SettingsMenuItem）",
                timeout: TimeSpan.FromSeconds(20));
            UiInteraction.InvokeOrClick(settingsItem);

            var settingsWindow = UiWait.ForElement(
                    () => TryFindSettingsWindow(app),
                    "设置窗口",
                    WindBoardApp.DialogTimeout)
                .AsWindow();
            UiInteraction.BringToFront(settingsWindow);
            return settingsWindow;
        }

        /// <summary>在设置窗口中切换到指定导航页（按 NavigationViewItem 的 AutomationId）。</summary>
        public static void NavigateSettingsPage(WindBoardApp app, Window settingsWindow, string navigationItemId)
        {
            var item = UiWait.ForElement(
                () => TryFindIn(app, settingsWindow, navigationItemId),
                $"设置导航项（{navigationItemId}）");

            var selection = item.Patterns.SelectionItem.PatternOrDefault;
            if (selection is not null)
            {
                selection.Select();
            }
            else
            {
                UiInteraction.ClickCenter(item);
            }
        }

        /// <summary>等待主窗口中某 AutomationId 元素出现。</summary>
        public static AutomationElement WaitForInMainWindow(WindBoardApp app, string automationId, TimeSpan? timeout = null)
        {
            return UiWait.ForElement(
                () => TryFindInMainWindow(app, automationId),
                $"主窗口元素（{automationId}）",
                timeout);
        }

        /// <summary>等待指定窗口中某 AutomationId 元素出现。</summary>
        public static AutomationElement WaitForInWindow(WindBoardApp app, Window window, string automationId, TimeSpan? timeout = null)
        {
            return UiWait.ForElement(
                () => TryFindIn(app, window, automationId),
                $"窗口元素（{automationId}）",
                timeout);
        }

        /// <summary>在桌面全局（含顶层弹层/系统对话框）等待某 AutomationId 元素出现。</summary>
        public static AutomationElement WaitForEverywhere(WindBoardApp app, string automationId, TimeSpan? timeout = null)
        {
            return UiWait.ForElement(
                () => TryFindEverywhere(app, automationId),
                $"全局元素（{automationId}）",
                timeout);
        }

        /// <summary>在容器内按显示文案等待按钮出现（用于无 AutomationId 的动态构建弹窗）。</summary>
        public static AutomationElement WaitForButtonByName(
            WindBoardApp app,
            FlaUI.Core.AutomationElements.AutomationElement container,
            string[] names,
            string fallbackAutomationId,
            string what,
            TimeSpan? timeout = null)
        {
            return UiWait.ForElement(
                () => TryFindButtonByName(app, container, names, fallbackAutomationId),
                what,
                timeout);
        }

        /// <summary>
        /// 查找覆盖确认弹窗按钮：遍历除主窗口外的全部可见顶层窗口，逐个转 UIA 元素后
        /// 限深查找（桌面全局条件搜索慢且不可靠，弹层 HWND 的 owner 关系也不稳定，实测）。
        /// </summary>
        public static AutomationElement? TryFindOverwriteButton(WindBoardApp app)
        {
            // ContentDialog 弹层就在主窗口 XAML 树内：按 Name 单条件查找（不限 ControlType）。
            try
            {
                return app.MainWindow.FindFirstDescendant(app.Cf.ByName(UiText.OverwriteConfirmButton[0]));
            }
            catch
            {
                return null;
            }
        }

        /// <summary>按显示文案立即查找按钮（非等待）；找不到返回 null。</summary>
        public static AutomationElement? TryFindButtonByName(
            WindBoardApp app,
            FlaUI.Core.AutomationElements.AutomationElement container,
            string[] names,
            string fallbackAutomationId = "")
        {
            try
            {
                return UiInteraction.TryFindDialogButton(container, app.Cf, names, fallbackAutomationId);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 桌面全局按显示文案查找按钮（ContentDialog 等弹层元素在主窗口树中不可靠，实测）。
        /// </summary>
        public static AutomationElement? TryFindButtonEverywhere(WindBoardApp app, string[] names)
        {
            try
            {
                var desktop = app.Automation.GetDesktop();
                foreach (string name in names)
                {
                    var condition = new FlaUI.Core.Conditions.AndCondition(
                        app.Cf.ByName(name),
                        app.Cf.ByControlType(FlaUI.Core.Definitions.ControlType.Button));
                    var element = desktop.FindFirstDescendant(condition);
                    if (element is not null)
                    {
                        return element;
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>立即查找主窗口中可见（未离屏）的元素；不存在/不可见/树失效均返回 null。</summary>
        public static AutomationElement? FindVisibleNow(WindBoardApp app, string automationId)
        {
            try
            {
                var element = app.MainWindow.FindFirstDescendant(app.Cf.ByAutomationId(automationId));
                if (element is null || element.Properties.IsOffscreen.ValueOrDefault)
                {
                    return null;
                }

                return element;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>点击 SettingsCard 形态的入口卡片（如伪装/Dock 设置入口）。</summary>
        public static void ClickCard(WindBoardApp app, Window settingsWindow, string cardAutomationId)
        {
            var card = UiWait.ForElement(
                () => TryFindIn(app, settingsWindow, cardAutomationId),
                $"设置入口卡片（{cardAutomationId}）");
            UiInteraction.InvokeOrClick(card);
        }

        /// <summary>
        /// 驱动系统文件对话框：写入完整路径并确认。
        /// 使用 Value 模式直接写入完整路径，绕过目录导航。
        /// </summary>
        public static void CompleteFileDialog(WindBoardApp app, string fullPath, bool save, TestArtifacts? artifacts = null)
        {
            Window dialog;
            try
            {
                dialog = UiWait.ForElement(
                    () => UiInteraction.TryFindFileDialog(app),
                    save ? "系统“保存”对话框" : "系统“打开”对话框",
                    WindBoardApp.DialogTimeout);
            }
            catch (TimeoutException)
            {
                // 诊断：文件对话框定位失败时记录桌面全部顶层窗口，便于确认真实 pid/class/标题。
                artifacts?.Step(UiInteraction.DumpDesktopWindows(app));
                throw;
            }

            // IFileDialog 的文件名编辑框：限深查找（全深度搜索会触发 UIA 内部超时）。
            var fileNameEdit = UiWait.ForElement(
                () => UiInteraction.FindVisibleDescendantLimitedDepth(
                    dialog,
                    FlaUI.Core.Definitions.ControlType.Edit,
                    maxDepth: 4),
                "文件名输入框");

            UiInteraction.SetValue(fileNameEdit, fullPath);

            string[] names = save ? UiText.SaveDialogConfirmButton : UiText.OpenDialogConfirmButton;
            var confirmButton = UiWait.ForElement(
                () => FindConfirmButtonShallow(dialog, names),
                "文件对话框确认按钮");
            UiInteraction.InvokeOrClick(confirmButton);
        }

        /// <summary>
        /// 在对话框内限深查找确认按钮：按显示文案前缀匹配。
        /// 说明：Win32 对话框按钮的 UIA Name 带快捷键后缀（如“打开(O)”），须用前缀匹配；
        /// 匹配不到时返回 null（宁等超时也不盲点其他按钮，如“取消”）。
        /// </summary>
        private static AutomationElement? FindConfirmButtonShallow(Window dialog, string[] names)
        {
            var buttons = CollectDescendantsLimitedDepth(dialog, FlaUI.Core.Definitions.ControlType.Button, maxDepth: 4);
            foreach (AutomationElement button in buttons)
            {
                try
                {
                    string? name = button.Properties.Name.ValueOrDefault;
                    if (name is not null
                        && names.Any(candidate => name.StartsWith(candidate, StringComparison.Ordinal)))
                    {
                        return button;
                    }
                }
                catch
                {
                    // 属性读取失败：跳过。
                }
            }

            return null;
        }

        private static System.Collections.Generic.IReadOnlyList<AutomationElement> CollectDescendantsLimitedDepth(
            AutomationElement root,
            FlaUI.Core.Definitions.ControlType targetType,
            int maxDepth)
        {
            var matches = new System.Collections.Generic.List<AutomationElement>();
            var frontier = new System.Collections.Generic.List<AutomationElement> { root };

            for (int depth = 0; depth < maxDepth && frontier.Count > 0; depth++)
            {
                var next = new System.Collections.Generic.List<AutomationElement>();

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
                            if (child.Properties.ControlType.ValueOrDefault == targetType)
                            {
                                matches.Add(child);
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

            return matches;
        }

        private static AutomationElement? TryFindInMainWindow(WindBoardApp app, string automationId)
        {
            try
            {
                return app.MainWindow.FindFirstDescendant(app.Cf.ByAutomationId(automationId));
            }
            catch
            {
                return null;
            }
        }

        private static AutomationElement? TryFindSettingsWindow(WindBoardApp app)
        {
            nint mainWindowHandle = app.MainWindow.Properties.NativeWindowHandle.ValueOrDefault;

            // 优先按标题匹配（避免误命中同进程的弹层宿主窗口），找不到再退回“非主窗口”兜底。
            foreach (string title in UiText.SettingsWindowTitle)
            {
                var byTitle = UiInteraction.TryFindTopLevelWindow(app, window =>
                    window.Properties.NativeWindowHandle.ValueOrDefault != mainWindowHandle
                    && string.Equals(window.Title, title, StringComparison.Ordinal));
                if (byTitle is not null)
                {
                    return byTitle;
                }
            }

            return UiInteraction.TryFindTopLevelWindow(app, window =>
                window.Properties.NativeWindowHandle.ValueOrDefault != mainWindowHandle);
        }

        private static AutomationElement? TryFind(WindBoardApp app, Func<AutomationElement?> finder)
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

        private static AutomationElement? TryFindIn(WindBoardApp app, Window window, string id)
        {
            try
            {
                return window.FindFirstDescendant(app.Cf.ByAutomationId(id));
            }
            catch
            {
                return null;
            }
        }

        private static AutomationElement? TryFindEverywhere(WindBoardApp app, string id)
        {
            // 主窗口先找；未命中再遍历其它顶层弹层 HWND 限深查找
            // （desktop 全树条件搜索在大桌面树上慢且间歇超时，实测不能依赖）。
            try
            {
                var inMain = app.MainWindow.FindFirstDescendant(app.Cf.ByAutomationId(id));
                if (inMain is not null)
                {
                    return inMain;
                }
            }
            catch
            {
                // 树失效：交给 retry。
            }

            nint mainHwnd = app.MainWindow.Properties.NativeWindowHandle.ValueOrDefault;

            foreach (nint hwnd in NativeWindowFinder.EnumVisibleTopLevelWindows())
            {
                if (hwnd == mainHwnd)
                {
                    continue;
                }

                try
                {
                    AutomationElement root = app.Automation.FromHandle(hwnd);
                    AutomationElement? found = UiInteraction.FindByAutomationIdLimitedDepth(root, id, maxDepth: 6);
                    if (found is not null)
                    {
                        return found;
                    }
                }
                catch
                {
                    // 个别 HWND 无法转 UIA 元素：跳过。
                }
            }

            return null;
        }
    }
}
