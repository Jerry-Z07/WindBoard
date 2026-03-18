using System;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI.Composition;

namespace WindBoard.Features.ScreenAnnotation.UI.Backdrop
{
    /// <summary>
    /// 基于 CompositionBrush 的自定义系统背景基类。
    /// </summary>
    internal abstract class ScreenAnnotationCompositionBrushBackdrop : SystemBackdrop
    {
        private static readonly object CompositorLock = new();
        private static Compositor? _sharedCompositor;

        /// <summary>
        /// 获取当前线程可复用的 Compositor。
        /// </summary>
        protected static Compositor SharedCompositor
        {
            get
            {
                if (_sharedCompositor is not null)
                {
                    return _sharedCompositor;
                }

                lock (CompositorLock)
                {
                    if (_sharedCompositor is null)
                    {
                        DispatcherQueue.GetForCurrentThread()?.EnsureSystemDispatcherQueue();
                        _sharedCompositor = new Compositor();
                    }
                }

                return _sharedCompositor;
            }
        }

        protected abstract CompositionBrush CreateBrush(Compositor compositor);

        protected override void OnDefaultSystemBackdropConfigurationChanged(
            Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop target,
            XamlRoot xamlRoot)
        {
            if (target is not null)
            {
                base.OnDefaultSystemBackdropConfigurationChanged(target, xamlRoot);
            }
        }

        protected override void OnTargetConnected(
            Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop connectedTarget,
            XamlRoot xamlRoot)
        {
            connectedTarget.SystemBackdrop = CreateBrush(SharedCompositor);
            base.OnTargetConnected(connectedTarget, xamlRoot);
        }

        protected override void OnTargetDisconnected(
            Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop disconnectedTarget)
        {
            CompositionBrush? brush = disconnectedTarget.SystemBackdrop;
            disconnectedTarget.SystemBackdrop = null;
            brush?.Dispose();
            base.OnTargetDisconnected(disconnectedTarget);
        }
    }
}
