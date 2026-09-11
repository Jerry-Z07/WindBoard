namespace WindBoard.UITests.Infrastructure
{
    /// <summary>
    /// 无法标注 AutomationId 的系统/动态构建 UI 的文案常量。
    /// 说明：应用默认语言为 zh-CN（资源缺失时回退），系统对话框文案取决于系统 UI 语言；
    /// 这里按“zh-CN 优先、英文兜底”提供候选，并由调用方以经典对话框 AutomationId 兜底。
    /// 若后续文案漂移，仅需修改本文件。
    /// </summary>
    public static class UiText
    {
        /// <summary>设置窗口标题（Common_Settings）。</summary>
        public static readonly string[] SettingsWindowTitle = { "设置", "Settings" };

        /// <summary>导出对话框主按钮（Common_Next）。</summary>
        public static readonly string[] ExportDialogPrimaryButton = { "下一步", "Next" };

        /// <summary>系统“保存”对话框确认按钮（经典 AutomationId=1 兜底）。</summary>
        public static readonly string[] SaveDialogConfirmButton = { "保存", "Save" };

        /// <summary>系统“打开”对话框确认按钮（经典 AutomationId=1 兜底）。</summary>
        public static readonly string[] OpenDialogConfirmButton = { "打开", "Open" };

        /// <summary>应用内单按钮提示弹窗（DialogHelpers.ShowMessageAsync 默认按钮，Common_Close → “关闭”/“Close”）。</summary>
        public static readonly string[] MessageBoxCloseButton = { "关闭", "Close" };

        /// <summary>导出覆盖确认弹窗的主按钮（Common_Overwrite）。</summary>
        public static readonly string[] OverwriteConfirmButton = { "覆盖", "Overwrite" };

        /// <summary>经典文件对话框确认按钮的 Win32 控件 ID 映射（UIA AutomationId）。</summary>
        public const string FileDialogConfirmButtonAutomationId = "1";
    }
}
