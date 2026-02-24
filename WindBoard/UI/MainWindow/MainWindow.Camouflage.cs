using System;
using Microsoft.UI.Xaml;
using WindBoard.Features.Camouflage;
using WindBoard.Logging;

namespace WindBoard
{
    public sealed partial class MainWindow : Window
    {
        private CamouflageFlow? _camouflageFlow;

        /// <summary>
        /// 仅保留入口薄层：具体实现由 Features/Camouflage/CamouflageFlow 负责。
        /// </summary>
        private void ApplyCamouflageSettingsToWindow()
        {
            try
            {
                GetOrCreateCamouflageFlow().ApplyToWindow();
            }
            catch (Exception ex)
            {
                // 兜底：避免异常冒泡到 UI 线程导致崩溃。
                AppLog.Error("Camouflage", "应用伪装设置失败。", ex);
            }
        }

        private CamouflageFlow GetOrCreateCamouflageFlow()
        {
            if (_camouflageFlow is not null)
            {
                return _camouflageFlow;
            }

            _camouflageFlow = new CamouflageFlow(
                dispatcherQueue: DispatcherQueue,
                tryGetHwnd: () =>
                {
                    try
                    {
                        return WinRT.Interop.WindowNative.GetWindowHandle(this);
                    }
                    catch
                    {
                        return IntPtr.Zero;
                    }
                },
                setWindowTitle: title => Title = title);

            return _camouflageFlow;
        }
    }
}
