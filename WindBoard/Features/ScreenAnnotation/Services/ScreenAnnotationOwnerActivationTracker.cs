using Microsoft.UI.Xaml;

namespace WindBoard.Features.ScreenAnnotation.Services
{
    /// <summary>
    /// 跟踪主窗口激活状态，用于判断屏幕批注是否需要在“回到主窗口”时自动退出。
    /// </summary>
    internal sealed class ScreenAnnotationOwnerActivationTracker
    {
        private bool _hasObservedOwnerDeactivated;

        /// <summary>
        /// 记录一次主窗口激活状态变化，并返回是否应退出屏幕批注。
        /// </summary>
        internal bool Observe(WindowActivationState activationState)
        {
            if (activationState == WindowActivationState.Deactivated)
            {
                _hasObservedOwnerDeactivated = true;
                return false;
            }

            return _hasObservedOwnerDeactivated;
        }
    }
}
